using Ardalis.Result;
using Extenso.Collections.Generic;
using Hangfire;
using MyVideoArchive.Models.Metadata;
using MyVideoArchive.Models.Requests.Playlist;
using MyVideoArchive.Models.Responses;
using MyVideoArchive.Services.Content;
using MyVideoArchive.Services.Jobs;

namespace MyVideoArchive.Services;

public class PlaylistService : IPlaylistService
{
    private readonly ILogger<PlaylistService> logger;
    private readonly IConfiguration configuration;
    private readonly IUserContextService userContextService;
    private readonly IBackgroundJobClient backgroundJobClient;
    private readonly ThumbnailService thumbnailService;
    private readonly VideoMetadataProviderFactory metadataProviderFactory;
    private readonly ITagService tagService;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<Playlist> playlistRepository;
    private readonly IRepository<PlaylistVideo> playlistVideoRepository;
    private readonly IRepository<UserChannel> userChannelRepository;
    private readonly IRepository<UserPlaylist> userPlaylistRepository;
    private readonly IRepository<UserPlaylistVideo> userPlaylistVideoRepository;
    private readonly IRepository<Video> videoRepository;
    private readonly IRepository<UserVideo> userVideoRepository;

    public PlaylistService(
        ILogger<PlaylistService> logger,
        IConfiguration configuration,
        IUserContextService userContextService,
        IBackgroundJobClient backgroundJobClient,
        VideoMetadataProviderFactory metadataProviderFactory,
        ThumbnailService thumbnailService,
        ITagService tagService,
        IRepository<Channel> channelRepository,
        IRepository<Playlist> playlistRepository,
        IRepository<PlaylistVideo> playlistVideoRepository,
        IRepository<UserChannel> userChannelRepository,
        IRepository<UserPlaylist> userPlaylistRepository,
        IRepository<UserPlaylistVideo> userPlaylistVideoRepository,
        IRepository<Video> videoRepository,
        IRepository<UserVideo> userVideoRepository)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.userContextService = userContextService;
        this.backgroundJobClient = backgroundJobClient;
        this.thumbnailService = thumbnailService;
        this.metadataProviderFactory = metadataProviderFactory;
        this.tagService = tagService;
        this.channelRepository = channelRepository;
        this.playlistRepository = playlistRepository;
        this.playlistVideoRepository = playlistVideoRepository;
        this.userChannelRepository = userChannelRepository;
        this.userPlaylistRepository = userPlaylistRepository;
        this.userPlaylistVideoRepository = userPlaylistVideoRepository;
        this.videoRepository = videoRepository;
        this.userVideoRepository = userVideoRepository;
    }

    public async Task<Result<IPagedCollection<AvailablePlaylistItem>>> GetAvailablePlaylistsAsync(
        int channelId,
        bool showIgnored = false,
        int page = 1,
        int pageSize = 24,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await UserHasAccessToChannelAsync(channelId))
            {
                return Result.Forbidden();
            }

            var channel = await channelRepository.FindOneAsync(channelId);
            if (channel is null)
            {
                return Result.NotFound("Channel not found");
            }

            var provider = metadataProviderFactory.GetProviderByPlatform(channel.Platform);
            if (provider is null)
            {
                return Result.Invalid([new ValidationError("Platform", $"No provider found for platform {channel.Platform}")]);
            }

            var predicate = PredicateBuilder.New<Playlist>(x => x.ChannelId == channelId);

            // Playlist.IsIgnored is the admin-level global block.
            // UserPlaylist.IsIgnored is a per-user personal hide.
            bool isAdmin = userContextService.IsAdministrator();
            string? currentUserId = userContextService.GetCurrentUserId();

            if (!showIgnored)
            {
                predicate = predicate.And(x => !x.IsIgnored);

                if (!isAdmin && !string.IsNullOrEmpty(currentUserId))
                {
                    var userIgnoredPlaylistIds = (await userPlaylistRepository.FindAsync(
                        new SearchOptions<UserPlaylist>
                        {
                            Query = x => x.UserId == currentUserId && x.IsIgnored
                        },
                        x => x.PlaylistId)).ToHashSet();

                    if (userIgnoredPlaylistIds.Count > 0)
                    {
                        predicate = predicate.And(x => !userIgnoredPlaylistIds.Contains(x.Id));
                    }
                }
            }

            var playlists = await playlistRepository.FindAsync(new SearchOptions<Playlist>
            {
                CancellationToken = cancellationToken,
                Query = predicate,
                OrderBy = showIgnored
                    ? query => query
                        .OrderBy(x => x.IsIgnored) // ignroed playlists last
                        .ThenBy(x => x.SubscribedAt == default)
                        .ThenBy(x => x.Name)
                    : query => query
                        .OrderBy(x => x.SubscribedAt == default)
                        .ThenBy(p => p.Name),
                PageNumber = page,
                PageSize = pageSize
            }, x => new AvailablePlaylistItem(
                x.Id,
                x.PlaylistId,
                x.Name,
                x.Description,
                x.Url,
                x.ThumbnailUrl,
                x.Platform,
                x.VideoCount,
                x.SubscribedAt,
                x.LastChecked,
                x.IsIgnored,
                x.SubscribedAt != default));

            return Result.Success(playlists);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving playlists for channel {ChannelId}", channelId);
            }

            return Result.Error("An error occurred while retrieving playlists");
        }
    }

    public async Task<Result<GetCustomOrderResponse>> GetCustomOrderAsync(int playlistId)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            var videoOrders = (await userPlaylistVideoRepository.FindAsync(new SearchOptions<UserPlaylistVideo>
            {
                Query = x =>
                    x.UserId == userId &&
                    x.PlaylistId == playlistId &&
                    x.CustomOrder > 0,
                OrderBy = query => query.OrderBy(x => x.CustomOrder)
            }, x => new VideoOrderItem(x.VideoId, x.CustomOrder))).ToList();

            return Result.Success(new GetCustomOrderResponse(videoOrders));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error getting custom order for playlist {PlaylistId}", playlistId);
            }

            return Result.Error("An error occurred while getting custom order");
        }
    }

    public async Task<Result<GetOrderSettingResponse>> GetOrderSettingAsync(int playlistId)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            var userPlaylist = await userPlaylistRepository.FindOneAsync(new SearchOptions<UserPlaylist>
            {
                Query = x => x.UserId == userId && x.PlaylistId == playlistId
            });

            bool useCustomOrder = userPlaylist?.UseCustomOrder ?? false;

            var orderRecords = await userPlaylistVideoRepository.FindAsync(new SearchOptions<UserPlaylistVideo>
            {
                Query = x =>
                    x.UserId == userId &&
                    x.PlaylistId == playlistId &&
                    x.CustomOrder > 0
            });

            bool hasCustomOrder = orderRecords.Count > 0;

            return Result.Success(new GetOrderSettingResponse(hasCustomOrder, useCustomOrder));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error getting order setting for playlist {PlaylistId}", playlistId);
            }

            return Result.Error("An error occurred while getting order setting");
        }
    }

    public async Task<Result<PlaylistOperationsVideosResponse>> GetPlaylistVideosAsync(int playlistId, bool useCustomOrder = false, bool showHidden = false)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            IEnumerable<PlaylistVideo> playlistVideos = await playlistVideoRepository.FindAsync(new SearchOptions<PlaylistVideo>
            {
                Query = x => x.PlaylistId == playlistId,
                Include = query => query
                    .Include(x => x.Video)
                    .ThenInclude(x => x.Channel),
                OrderBy = query => query.OrderBy(x => x.Order)
            });

            var userPlaylistVideos = await userPlaylistVideoRepository.FindAsync(new SearchOptions<UserPlaylistVideo>
            {
                Query = x => x.UserId == userId && x.PlaylistId == playlistId
            });

            var hiddenVideoIds = userPlaylistVideos
                .Where(x => x.IsHidden)
                .Select(x => x.VideoId)
                .ToHashSet();

            bool hasCustomOrder = userPlaylistVideos.Any(x => x.CustomOrder > 0);

            if (useCustomOrder && hasCustomOrder)
            {
                var orderMap = userPlaylistVideos
                    .Where(x => x.CustomOrder > 0)
                    .ToDictionary(x => x.VideoId, x => x.CustomOrder);

                playlistVideos = playlistVideos
                    .OrderBy(x => orderMap.TryGetValue(x.VideoId, out int order) ? order : int.MaxValue)
                    .ToList();
            }

            if (!showHidden)
            {
                playlistVideos = playlistVideos
                    .Where(x => !hiddenVideoIds.Contains(x.VideoId))
                    .ToList();
            }

            var videos = playlistVideos.Select(x => new PlaylistVideoItem(
                x.Video.Id,
                x.Video.VideoId,
                (x.Video.Title is Constants.PrivateVideoTitle or Constants.DeletedVideoTitle)
                    ? $"{x.Video.Title} - {x.Video.VideoId}"
                    : x.Video.Title,
                x.Video.Description,
                x.Video.Url,
                x.Video.ThumbnailUrl,
                x.Video.Duration,
                x.Video.UploadDate,
                x.Video.ViewCount,
                x.Video.LikeCount,
                x.Video.DownloadedAt,
                x.Video.IsIgnored,
                x.Video.IsQueued,
                x.Video.DownloadFailed,
                x.Video.ChannelId,
                new ChannelInfo(x.Video.Channel.Id, x.Video.Channel.Name),
                hiddenVideoIds.Contains(x.VideoId))).ToList();

            return Result.Success(new PlaylistOperationsVideosResponse(videos));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error getting videos for playlist {PlaylistId}", playlistId);
            }

            return Result.Error("An error occurred while getting playlist videos");
        }
    }

    public async Task<Result<RefreshPlaylistsResponse>> RefreshPlaylistsAsync(
        int channelId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await UserHasAccessToChannelAsync(channelId))
            {
                return Result.Forbidden();
            }

            var channel = await channelRepository.FindOneAsync(channelId);
            if (channel is null)
            {
                return Result.NotFound("Channel not found");
            }

            var provider = metadataProviderFactory.GetProviderByPlatform(channel.Platform);
            if (provider is null)
            {
                return Result.Invalid([new ValidationError("Platform", $"No provider found for platform {channel.Platform}")]);
            }

            var playlistMetadataList = await provider.GetChannelPlaylistsAsync(channel.Url);

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Found {Count} playlists for channel {ChannelId}", playlistMetadataList.Count, channelId);
            }

            int newPlaylistsCount = 0;
            var existingPlaylists = await playlistRepository.FindAsync(new SearchOptions<Playlist>
            {
                Query = x => x.ChannelId == channelId
            });

            var existingPlaylistIds = existingPlaylists.Select(p => p.PlaylistId).ToHashSet();
            string downloadPath = configuration.GetValue<string>("VideoDownload:OutputPath")
                ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

            var playlistUpdates = new List<Playlist>();
            var playlistInserts = new List<Playlist>();

            foreach (var playlistMetadata in playlistMetadataList)
            {
                string playlistThumbnailDir = Path.Combine(downloadPath, channel.ChannelId, "Playlists");

                if (existingPlaylistIds.Contains(playlistMetadata.PlaylistId))
                {
                    var existingPlaylist = existingPlaylists.First(x => x.PlaylistId == playlistMetadata.PlaylistId);
                    existingPlaylist.Name = playlistMetadata.Name;
                    existingPlaylist.Description = playlistMetadata.Description;
                    existingPlaylist.Url = playlistMetadata.Url;

                    if (!ThumbnailService.IsLocalUrl(existingPlaylist.ThumbnailUrl))
                    {
                        string? localUrl = await thumbnailService.DownloadAndSaveAsync(
                            playlistMetadata.ThumbnailUrl, playlistThumbnailDir, existingPlaylist.PlaylistId,
                            downloadPath);
                        existingPlaylist.ThumbnailUrl = localUrl ?? playlistMetadata.ThumbnailUrl;
                    }

                    playlistUpdates.Add(existingPlaylist);
                }
                else
                {
                    string? localThumbnailUrl = await thumbnailService.DownloadAndSaveAsync(
                        playlistMetadata.ThumbnailUrl, playlistThumbnailDir, playlistMetadata.PlaylistId,
                        downloadPath);

                    playlistInserts.Add(new Playlist
                    {
                        PlaylistId = playlistMetadata.PlaylistId,
                        Name = playlistMetadata.Name,
                        Description = playlistMetadata.Description,
                        Url = playlistMetadata.Url,
                        ThumbnailUrl = localThumbnailUrl ?? playlistMetadata.ThumbnailUrl,
                        Platform = playlistMetadata.Platform,
                        SubscribedAt = DateTime.MinValue,
                        IsIgnored = false,
                        ChannelId = channelId
                    });
                    newPlaylistsCount++;
                }
            }

            await playlistRepository.InsertAsync(playlistInserts);
            await playlistRepository.UpdateAsync(playlistUpdates);

            return Result.Success(new RefreshPlaylistsResponse(
                $"Refreshed playlists. Found {playlistMetadataList.Count} total, {newPlaylistsCount} new",
                playlistMetadataList.Count,
                newPlaylistsCount));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error refreshing playlists for channel {ChannelId}", channelId);
            }

            return Result.Error("An error occurred while refreshing playlists");
        }
    }

    public async Task<Result> SaveCustomOrderAsync(int playlistId, ReorderVideosRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            var userPlaylist = await userPlaylistRepository.FindOneAsync(new SearchOptions<UserPlaylist>
            {
                Query = x => x.UserId == userId && x.PlaylistId == playlistId
            });

            if (userPlaylist is null)
            {
                if (!userContextService.IsAdministrator())
                {
                    return Result.Forbidden();
                }

                userPlaylist = new UserPlaylist
                {
                    UserId = userId,
                    PlaylistId = playlistId,
                    SubscribedAt = DateTime.UtcNow
                };
                await userPlaylistRepository.InsertAsync(userPlaylist);
            }

            userPlaylist.UseCustomOrder = request.UseCustomOrder;
            await userPlaylistRepository.UpdateAsync(userPlaylist);

            if (request.UseCustomOrder && request.VideoOrders is not null)
            {
                var existingRecords = await userPlaylistVideoRepository.FindAsync(new SearchOptions<UserPlaylistVideo>
                {
                    Query = x => x.UserId == userId && x.PlaylistId == playlistId
                });

                var hiddenVideoIds = existingRecords
                    .Where(x => x.IsHidden)
                    .Select(x => x.VideoId)
                    .ToHashSet();

                foreach (var record in existingRecords)
                {
                    await userPlaylistVideoRepository.DeleteAsync(record);
                }

                foreach (var videoOrder in request.VideoOrders)
                {
                    await userPlaylistVideoRepository.InsertAsync(new UserPlaylistVideo
                    {
                        UserId = userId,
                        PlaylistId = playlistId,
                        VideoId = videoOrder.VideoId,
                        CustomOrder = videoOrder.Order,
                        IsHidden = false
                    });
                }

                foreach (int hiddenVideoId in hiddenVideoIds)
                {
                    await userPlaylistVideoRepository.InsertAsync(new UserPlaylistVideo
                    {
                        UserId = userId,
                        PlaylistId = playlistId,
                        VideoId = hiddenVideoId,
                        CustomOrder = 0,
                        IsHidden = true
                    });
                }
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error saving custom order for playlist {PlaylistId}", playlistId);
            }

            return Result.Error("An error occurred while saving custom order");
        }
    }

    public async Task<Result> SetVideoHiddenAsync(int playlistId, int videoId, SetVideoHiddenRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            var existing = await userPlaylistVideoRepository.FindOneAsync(new SearchOptions<UserPlaylistVideo>
            {
                Query = x =>
                    x.UserId == userId &&
                    x.PlaylistId == playlistId &&
                    x.VideoId == videoId
            });

            if (existing is not null)
            {
                existing.IsHidden = request.IsHidden;
                await userPlaylistVideoRepository.UpdateAsync(existing);
            }
            else
            {
                await userPlaylistVideoRepository.InsertAsync(new UserPlaylistVideo
                {
                    UserId = userId,
                    PlaylistId = playlistId,
                    VideoId = videoId,
                    CustomOrder = 0,
                    IsHidden = request.IsHidden
                });
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error setting hidden status for video {VideoId} in playlist {PlaylistId}", videoId, playlistId);
            }

            return Result.Error("An error occurred while updating video visibility");
        }
    }

    public async Task<Result<SubscribePlaylistsResponse>> SubscribeAllPlaylistsAsync(int channelId)
    {
        try
        {
            if (!await UserHasAccessToChannelAsync(channelId))
            {
                return Result.Forbidden();
            }

            string? userId = userContextService.GetCurrentUserId();

            var playlists = await playlistRepository.FindAsync(new SearchOptions<Playlist>
            {
                Query = x => x.ChannelId == channelId && !x.IsIgnored
            });

            if (playlists.Count == 0)
            {
                return Result.Success(new SubscribePlaylistsResponse(
                    "No playlists available to subscribe",
                    0));
            }

            var playlistIds = playlists.Select(p => p.Id).ToList();
            var userPlaylistSubscriptions = await userPlaylistRepository.FindAsync(new SearchOptions<UserPlaylist>
            {
                Query = x => x.UserId == userId && playlistIds.Contains(x.PlaylistId)
            });

            var playlistUpdates = new List<Playlist>();
            var userPlaylistInserts = new List<UserPlaylist>();

            foreach (var playlist in playlists)
            {
                if (playlist.SubscribedAt == DateTime.MinValue)
                {
                    playlist.SubscribedAt = DateTime.UtcNow;
                    playlistUpdates.Add(playlist);
                }

                var existingSubscription = userPlaylistSubscriptions.FirstOrDefault(x => x.PlaylistId == playlist.Id);
                if (existingSubscription is null)
                {
                    userPlaylistInserts.Add(new UserPlaylist
                    {
                        UserId = userId!,
                        PlaylistId = playlist.Id,
                        SubscribedAt = DateTime.UtcNow
                    });
                }

                // Auto-import tags from playlist metadata (only when enabled and only on first subscribe)
                bool importTags = configuration.GetValue<bool>("Tags:ImportTagsFromPlatform", false);
                if (importTags)
                {
                    var channel = await channelRepository.FindOneAsync(channelId);
                    if (channel is not null)
                    {
                        var provider = metadataProviderFactory.GetProviderByPlatform(channel.Platform);
                        if (provider is not null)
                        {
                            var playlistMeta = await provider.GetPlaylistMetadataAsync(playlist.Url);
                            if (playlistMeta?.Tags.Count > 0)
                            {
                                await tagService.ImportPlaylistTagsAsync(playlist.Id, playlistMeta.Tags);
                            }
                        }
                    }
                }

                backgroundJobClient.Enqueue<PlaylistSyncJob>(job =>
                    job.ExecuteAsync(playlist.Id, CancellationToken.None));
            }

            await playlistRepository.UpdateAsync(playlistUpdates);
            await userPlaylistRepository.InsertAsync(userPlaylistInserts);

            return Result.Success(new SubscribePlaylistsResponse(
                $"Queued sync for {playlists.Count} playlist(s)",
                playlists.Count));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error subscribing to all playlists for channel {ChannelId}", channelId);
            }

            return Result.Error("An error occurred while subscribing to playlists");
        }
    }

    public async Task<Result<SubscribePlaylistsResponse>> SubscribePlaylistsAsync(
        int channelId,
        SubscribePlaylistsRequest request)
    {
        try
        {
            if (!await UserHasAccessToChannelAsync(channelId))
            {
                return Result.Forbidden();
            }

            if (request.PlaylistIds.IsNullOrEmpty())
            {
                return Result.Invalid([new ValidationError("PlaylistIds", "No playlist IDs provided")]);
            }

            var channel = await channelRepository.FindOneAsync(channelId);
            if (channel is null)
            {
                return Result.NotFound("Channel not found");
            }

            string? userId = userContextService.GetCurrentUserId();
            int subscribedCount = 0;

            foreach (int playlistId in request.PlaylistIds)
            {
                var playlist = await playlistRepository.FindOneAsync(playlistId);
                if (playlist is not null && playlist.ChannelId == channelId)
                {
                    if (playlist.SubscribedAt == DateTime.MinValue)
                    {
                        playlist.SubscribedAt = DateTime.UtcNow;
                        await playlistRepository.UpdateAsync(playlist);
                    }

                    var existingSubscription = await userPlaylistRepository.FindOneAsync(new SearchOptions<UserPlaylist>
                    {
                        Query = x => x.UserId == userId && x.PlaylistId == playlistId
                    });

                    if (existingSubscription is null)
                    {
                        await userPlaylistRepository.InsertAsync(new UserPlaylist
                        {
                            UserId = userId!,
                            PlaylistId = playlistId,
                            SubscribedAt = DateTime.UtcNow
                        });
                    }

                    backgroundJobClient.Enqueue<PlaylistSyncJob>(job =>
                        job.ExecuteAsync(playlist.Id, CancellationToken.None));
                    subscribedCount++;
                }
            }

            return Result.Success(new SubscribePlaylistsResponse(
                $"Queued sync for {subscribedCount} playlist(s)",
                subscribedCount));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error subscribing to playlists for channel {ChannelId}", channelId);
            }

            return Result.Error("An error occurred while subscribing to playlists");
        }
    }

    public async Task<Result> SyncPlaylistAsync(int playlistId)
    {
        try
        {
            var playlist = await playlistRepository.FindOneAsync(playlistId);
            if (playlist is null)
            {
                return Result.NotFound("Playlist not found");
            }

            if (!await UserHasAccessToChannelAsync(playlist.ChannelId))
            {
                return Result.Forbidden();
            }

            backgroundJobClient.Enqueue<PlaylistSyncJob>(job =>
                job.ExecuteAsync(playlist.Id, CancellationToken.None));

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Queued sync job for playlist {PlaylistId}", playlistId);
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error queueing sync job for playlist {PlaylistId}", playlistId);
            }

            return Result.Error("An error occurred while queueing the playlist sync job");
        }
    }

    public Result SyncAllPlaylists()
    {
        try
        {
            backgroundJobClient.Enqueue<PlaylistSyncJob>(job =>
                job.SyncAllPlaylistsAsync(CancellationToken.None));

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Queued sync job for all playlists");
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error queueing sync job for all playlists");
            }

            return Result.Error("An error occurred while queueing the sync job");
        }
    }

    public async Task<Result<ToggleIgnorePlaylistResponse>> ToggleIgnoreAsync(
        int channelId,
        int playlistId,
        IgnorePlaylistRequest request)
    {
        try
        {
            if (!await UserHasAccessToChannelAsync(channelId))
            {
                return Result.Forbidden();
            }

            string? userId = userContextService.GetCurrentUserId();
            bool isAdmin = userContextService.IsAdministrator();

            if (isAdmin)
            {
                // Admin: global block — hides the playlist for all users.
                var playlist = await playlistRepository.FindOneAsync(new SearchOptions<Playlist>
                {
                    Query = x => x.Id == playlistId && x.ChannelId == channelId
                });

                if (playlist is null)
                {
                    return Result.NotFound("Playlist not found");
                }

                playlist.IsIgnored = request.IsIgnored;
                await playlistRepository.UpdateAsync(playlist);

                // Optionally cascade ignore to all not-yet-downloaded videos in this playlist.
                if (request.IsIgnored && request.IgnoreVideos)
                {
                    // If the playlist has never been synced, PlaylistVideo records may not exist yet.
                    // Fetch them from the provider now so the cascade can take effect.
                    bool hasAnyPlaylistVideos = await playlistVideoRepository.ExistsAsync(x => x.PlaylistId == playlistId);
                    if (!hasAnyPlaylistVideos)
                    {
                        await EnsurePlaylistVideosPopulatedAsync(playlist, CancellationToken.None);
                    }

                    var notDownloadedVideoIds = await playlistVideoRepository.FindAsync(
                        new SearchOptions<PlaylistVideo>
                        {
                            Query = x => x.PlaylistId == playlistId && x.Video.DownloadedAt == null,
                            Include = query => query.Include(x => x.Video)
                        },
                        x => x.VideoId);

                    if (notDownloadedVideoIds.Count > 0)
                    {
                        await videoRepository.UpdateAsync(
                            x => notDownloadedVideoIds.Contains(x.Id) && x.DownloadedAt == null,
                            setters => setters.SetProperty(x => x.IsIgnored, true));
                    }
                }

                return Result.Success(new ToggleIgnorePlaylistResponse(
                    request.IsIgnored ? "Playlist ignored" : "Playlist unignored",
                    playlist.IsIgnored));
            }
            else
            {
                // Non-admin: personal hide via UserPlaylist.IsIgnored.
                var playlist = await playlistRepository.FindOneAsync(new SearchOptions<Playlist>
                {
                    Query = x => x.Id == playlistId && x.ChannelId == channelId
                });

                if (playlist is null)
                {
                    return Result.NotFound("Playlist not found");
                }

                var userPlaylist = await userPlaylistRepository.FindOneAsync(new SearchOptions<UserPlaylist>
                {
                    Query = x => x.UserId == userId && x.PlaylistId == playlistId
                });

                if (userPlaylist is not null)
                {
                    userPlaylist.IsIgnored = request.IsIgnored;
                    await userPlaylistRepository.UpdateAsync(userPlaylist);
                }
                else
                {
                    await userPlaylistRepository.InsertAsync(new UserPlaylist
                    {
                        UserId = userId!,
                        PlaylistId = playlistId,
                        IsIgnored = request.IsIgnored
                    });
                }

                // Optionally cascade ignore to all not-yet-downloaded videos in this playlist (per-user).
                if (request.IsIgnored && request.IgnoreVideos)
                {
                    // If the playlist has never been synced, PlaylistVideo records may not exist yet.
                    bool hasAnyPlaylistVideos = await playlistVideoRepository.ExistsAsync(x => x.PlaylistId == playlistId);
                    if (!hasAnyPlaylistVideos)
                    {
                        await EnsurePlaylistVideosPopulatedAsync(playlist, CancellationToken.None);
                    }

                    var notDownloadedVideoIds = await playlistVideoRepository.FindAsync(
                        new SearchOptions<PlaylistVideo>
                        {
                            Query = x => x.PlaylistId == playlistId && x.Video.DownloadedAt == null,
                            Include = q => q.Include(x => x.Video)
                        },
                        x => x.VideoId);

                    foreach (int videoId in notDownloadedVideoIds)
                    {
                        var existingUserVideo = await userVideoRepository.FindOneAsync(new SearchOptions<UserVideo>
                        {
                            Query = x => x.UserId == userId && x.VideoId == videoId
                        });

                        if (existingUserVideo is not null)
                        {
                            existingUserVideo.IsIgnored = true;
                            await userVideoRepository.UpdateAsync(existingUserVideo);
                        }
                        else
                        {
                            await userVideoRepository.InsertAsync(new UserVideo
                            {
                                UserId = userId!,
                                VideoId = videoId,
                                IsIgnored = true
                            });
                        }
                    }
                }

                return Result.Success(new ToggleIgnorePlaylistResponse(
                    request.IsIgnored ? "Playlist ignored" : "Playlist unignored",
                    request.IsIgnored));
            }
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error toggling ignore status for playlist {PlaylistId}", playlistId);
            }

            return Result.Error("An error occurred while updating playlist status");
        }
    }

    /// <summary>
    /// Fetches videos for a playlist from the metadata provider and creates Video + PlaylistVideo
    /// records for any that don't already exist. Called when a playlist has never been synced and
    /// a user requests a cascade-ignore, so there are no PlaylistVideo records to act on yet.
    /// </summary>
    private async Task EnsurePlaylistVideosPopulatedAsync(Playlist playlist, CancellationToken cancellationToken)
    {
        var provider = metadataProviderFactory.GetProviderByPlatform(playlist.Platform);
        if (provider is null)
        {
            return;
        }

        List<VideoMetadata> videoMetadataList;
        try
        {
            var rawList = await provider.GetPlaylistVideosAsync(playlist.Url, cancellationToken);
            var seenKeys = new HashSet<(string Platform, string VideoId)>();
            videoMetadataList = rawList.Where(v => seenKeys.Add((v.Platform, v.VideoId))).ToList();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(ex, "Failed to fetch playlist videos for playlist {PlaylistId} during ignore cascade", playlist.Id);
            }

            return;
        }

        var playlistVideoInserts = new List<PlaylistVideo>();
        int order = 0;

        foreach (var videoMeta in videoMetadataList)
        {
            order++;

            var existingVideo = await videoRepository.FindOneAsync(new SearchOptions<Video>
            {
                CancellationToken = cancellationToken,
                Query = x => x.Platform == videoMeta.Platform && x.VideoId == videoMeta.VideoId
            });

            if (existingVideo is not null)
            {
                bool alreadyLinked = await playlistVideoRepository.ExistsAsync(
                    x => x.PlaylistId == playlist.Id && x.VideoId == existingVideo.Id,
                    ContextOptions.ForCancellationToken(cancellationToken));

                if (!alreadyLinked)
                {
                    playlistVideoInserts.Add(new PlaylistVideo
                    {
                        PlaylistId = playlist.Id,
                        VideoId = existingVideo.Id,
                        Order = order
                    });
                }
            }
            else
            {
                var newVideo = new Video
                {
                    VideoId = videoMeta.VideoId,
                    Title = videoMeta.Title,
                    Description = videoMeta.Description,
                    Url = videoMeta.Url,
                    ThumbnailUrl = videoMeta.ThumbnailUrl,
                    Platform = videoMeta.Platform,
                    Duration = videoMeta.Duration,
                    UploadDate = videoMeta.UploadDate,
                    ViewCount = videoMeta.ViewCount,
                    LikeCount = videoMeta.LikeCount,
                    ChannelId = playlist.ChannelId,
                    IsIgnored = false,
                    IsQueued = false,
                    NeedsMetadataReview = videoMeta.Title == Constants.PrivateVideoTitle
                };

                await videoRepository.InsertAsync(newVideo, ContextOptions.ForCancellationToken(cancellationToken));

                playlistVideoInserts.Add(new PlaylistVideo
                {
                    PlaylistId = playlist.Id,
                    VideoId = newVideo.Id,
                    Order = order
                });
            }
        }

        if (playlistVideoInserts.Count > 0)
        {
            await playlistVideoRepository.InsertAsync(playlistVideoInserts, ContextOptions.ForCancellationToken(cancellationToken));
        }
    }

    public async Task<Result<SubscribePlaylistsResponse>> ImportPlaylistByUrlAsync(
        int channelId,
        string playlistUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await UserHasAccessToChannelAsync(channelId))
            {
                return Result.Forbidden();
            }

            if (string.IsNullOrWhiteSpace(playlistUrl))
            {
                return Result.Invalid([new ValidationError("playlistUrl", "Playlist URL is required")]);
            }

            var channel = await channelRepository.FindOneAsync(channelId);
            if (channel is null)
            {
                return Result.NotFound("Channel not found");
            }

            var provider = metadataProviderFactory.GetProvider(playlistUrl);
            if (provider is null)
            {
                return Result.Invalid([new ValidationError("playlistUrl", "No provider found for this playlist URL")]);
            }

            var playlistMeta = await provider.GetPlaylistMetadataAsync(playlistUrl, cancellationToken);
            if (playlistMeta is null)
            {
                return Result.Invalid([new ValidationError("playlistUrl", "Could not retrieve playlist metadata. Please check the URL and try again.")]);
            }

            // Verify the playlist belongs to this channel
            if (!string.Equals(playlistMeta.ChannelId, channel.ChannelId, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Invalid([new ValidationError("playlistUrl",
                    $"This playlist belongs to a different channel ('{playlistMeta.ChannelName}'). " +
                    "Please add a playlist that belongs to this channel.")]);
            }

            string? userId = userContextService.GetCurrentUserId();
            string downloadPath = configuration.GetValue<string>("VideoDownload:OutputPath")
                ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");
            string playlistThumbnailDir = Path.Combine(downloadPath, channel.ChannelId, "Playlists");

            // Find or create the playlist record
            var existing = await playlistRepository.FindOneAsync(new SearchOptions<Playlist>
            {
                Query = x => x.PlaylistId == playlistMeta.PlaylistId && x.ChannelId == channelId
            });

            Playlist playlist;

            if (existing is not null)
            {
                existing.Name = playlistMeta.Name;
                existing.Description = playlistMeta.Description;
                existing.Url = playlistMeta.Url;

                if (!ThumbnailService.IsLocalUrl(existing.ThumbnailUrl))
                {
                    string? localUrl = await thumbnailService.DownloadAndSaveAsync(
                        playlistMeta.ThumbnailUrl, playlistThumbnailDir, existing.PlaylistId, downloadPath);
                    existing.ThumbnailUrl = localUrl ?? playlistMeta.ThumbnailUrl;
                }

                if (existing.SubscribedAt == DateTime.MinValue)
                {
                    existing.SubscribedAt = DateTime.UtcNow;
                }

                await playlistRepository.UpdateAsync(existing);
                playlist = existing;
            }
            else
            {
                string? localThumbnailUrl = await thumbnailService.DownloadAndSaveAsync(
                    playlistMeta.ThumbnailUrl, playlistThumbnailDir, playlistMeta.PlaylistId, downloadPath);

                playlist = new Playlist
                {
                    PlaylistId = playlistMeta.PlaylistId,
                    Name = playlistMeta.Name,
                    Description = playlistMeta.Description,
                    Url = playlistMeta.Url,
                    ThumbnailUrl = localThumbnailUrl ?? playlistMeta.ThumbnailUrl,
                    Platform = playlistMeta.Platform,
                    SubscribedAt = DateTime.UtcNow,
                    IsIgnored = false,
                    ChannelId = channelId
                };
                await playlistRepository.InsertAsync(playlist);
            }

            // Subscribe the current user if not already subscribed
            var existingSubscription = await userPlaylistRepository.FindOneAsync(new SearchOptions<UserPlaylist>
            {
                Query = x => x.UserId == userId && x.PlaylistId == playlist.Id
            });

            if (existingSubscription is null)
            {
                await userPlaylistRepository.InsertAsync(new UserPlaylist
                {
                    UserId = userId!,
                    PlaylistId = playlist.Id,
                    SubscribedAt = DateTime.UtcNow
                });
            }

            // Import tags from playlist metadata (only when enabled and only on first subscribe)
            bool importTags = configuration.GetValue<bool>("Tags:ImportTagsFromPlatform", false);
            if (importTags && playlistMeta.Tags.Count > 0)
            {
                await tagService.ImportPlaylistTagsAsync(playlist.Id, playlistMeta.Tags);
            }

            // Queue sync job to pull in all videos
            backgroundJobClient.Enqueue<PlaylistSyncJob>(job =>
                job.ExecuteAsync(playlist.Id, CancellationToken.None));

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Imported playlist {PlaylistId} ({PlaylistName}) into channel {ChannelId}",
                    playlist.PlaylistId, playlist.Name, channelId);
            }

            return Result.Success(new SubscribePlaylistsResponse(
                $"Playlist '{playlist.Name}' imported successfully. Syncing videos in the background.",
                1));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error importing playlist by URL for channel {ChannelId}", channelId);
            }

            return Result.Error("An error occurred while importing the playlist");
        }
    }

    private async Task<bool> UserHasAccessToChannelAsync(int channelId)
    {
        if (userContextService.IsAdministrator())
        {
            return true;
        }

        string? userId = userContextService.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return false;
        }

        var userChannel = await userChannelRepository.FindOneAsync(new SearchOptions<UserChannel>
        {
            Query = x => x.UserId == userId && x.ChannelId == channelId
        });

        return userChannel is not null;
    }
}