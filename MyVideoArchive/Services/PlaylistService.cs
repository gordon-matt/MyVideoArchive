using Ardalis.Result;
using Extenso.Collections.Generic;
using Hangfire;
using MyVideoArchive.Models.Requests.Playlist;
using MyVideoArchive.Models.Responses;

namespace MyVideoArchive.Services;

public class PlaylistService : IPlaylistService
{
    private readonly ILogger<PlaylistService> logger;
    private readonly IConfiguration configuration;
    private readonly IUserContextService userContextService;
    private readonly IBackgroundJobClient backgroundJobClient;
    private readonly ThumbnailService thumbnailService;
    private readonly VideoMetadataProviderFactory metadataProviderFactory;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<Playlist> playlistRepository;
    private readonly IRepository<PlaylistVideo> playlistVideoRepository;
    private readonly IRepository<UserChannel> userChannelRepository;
    private readonly IRepository<UserPlaylist> userPlaylistRepository;
    private readonly IRepository<UserPlaylistVideo> userPlaylistVideoRepository;

    public PlaylistService(
        ILogger<PlaylistService> logger,
        IConfiguration configuration,
        IUserContextService userContextService,
        IBackgroundJobClient backgroundJobClient,
        VideoMetadataProviderFactory metadataProviderFactory,
        ThumbnailService thumbnailService,
        IRepository<Channel> channelRepository,
        IRepository<Playlist> playlistRepository,
        IRepository<PlaylistVideo> playlistVideoRepository,
        IRepository<UserChannel> userChannelRepository,
        IRepository<UserPlaylist> userPlaylistRepository,
        IRepository<UserPlaylistVideo> userPlaylistVideoRepository)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.userContextService = userContextService;
        this.backgroundJobClient = backgroundJobClient;
        this.thumbnailService = thumbnailService;
        this.metadataProviderFactory = metadataProviderFactory;
        this.channelRepository = channelRepository;
        this.playlistRepository = playlistRepository;
        this.playlistVideoRepository = playlistVideoRepository;
        this.userChannelRepository = userChannelRepository;
        this.userPlaylistRepository = userPlaylistRepository;
        this.userPlaylistVideoRepository = userPlaylistVideoRepository;
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
            if (!showIgnored)
            {
                predicate = predicate.And(x => !x.IsIgnored);
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
                if (existingPlaylistIds.Contains(playlistMetadata.PlaylistId))
                {
                    var existingPlaylist = existingPlaylists.First(x => x.PlaylistId == playlistMetadata.PlaylistId);
                    existingPlaylist.Name = playlistMetadata.Name;
                    existingPlaylist.Description = playlistMetadata.Description;
                    existingPlaylist.Url = playlistMetadata.Url;

                    if (!existingPlaylist.ThumbnailUrl?.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ?? true)
                    {
                        string playlistThumbnailDir = Path.Combine(downloadPath, channel.ChannelId, "Playlists");
                        existingPlaylist.ThumbnailUrl = await thumbnailService.DownloadAndSaveAsync(
                            playlistMetadata.ThumbnailUrl, playlistThumbnailDir, existingPlaylist.PlaylistId)
                            ?? playlistMetadata.ThumbnailUrl;
                    }

                    playlistUpdates.Add(existingPlaylist);
                }
                else
                {
                    playlistInserts.Add(new Playlist
                    {
                        PlaylistId = playlistMetadata.PlaylistId,
                        Name = playlistMetadata.Name,
                        Description = playlistMetadata.Description,
                        Url = playlistMetadata.Url,
                        ThumbnailUrl = playlistMetadata.ThumbnailUrl,
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

            return Result.Success(new ToggleIgnorePlaylistResponse(
                request.IsIgnored ? "Playlist ignored" : "Playlist unignored",
                playlist.IsIgnored));
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