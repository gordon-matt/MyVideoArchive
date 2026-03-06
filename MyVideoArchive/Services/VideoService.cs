using Ardalis.Result;
using Hangfire;
using MyVideoArchive.Models.Metadata;
using MyVideoArchive.Models.Responses;
using MyVideoArchive.Models.Video;

namespace MyVideoArchive.Services;

public class VideoService : IVideoService
{
    private readonly ILogger<VideoService> logger;
    private readonly IBackgroundJobClient backgroundJobClient;
    private readonly IUserContextService userContextService;
    private readonly VideoMetadataProviderFactory metadataProviderFactory;
    private readonly IChannelService channelService;
    private readonly ICustomPlaylistService customPlaylistService;
    private readonly ITagService tagService;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<PlaylistVideo> playlistVideoRepository;
    private readonly IRepository<UserChannel> userChannelRepository;
    private readonly IRepository<UserVideo> userVideoRepository;
    private readonly IRepository<Video> videoRepository;
    private readonly IRepository<VideoTag> videoTagRepository;

    public VideoService(
        ILogger<VideoService> logger,
        IUserContextService userContextService,
        IBackgroundJobClient backgroundJobClient,
        VideoMetadataProviderFactory metadataProviderFactory,
        IChannelService channelService,
        ICustomPlaylistService customPlaylistService,
        ITagService tagService,
        IRepository<Channel> channelRepository,
        IRepository<PlaylistVideo> playlistVideoRepository,
        IRepository<UserChannel> userChannelRepository,
        IRepository<UserVideo> userVideoRepository,
        IRepository<Video> videoRepository,
        IRepository<VideoTag> videoTagRepository)
    {
        this.logger = logger;
        this.userContextService = userContextService;
        this.backgroundJobClient = backgroundJobClient;
        this.metadataProviderFactory = metadataProviderFactory;
        this.channelService = channelService;
        this.customPlaylistService = customPlaylistService;
        this.tagService = tagService;
        this.channelRepository = channelRepository;
        this.playlistVideoRepository = playlistVideoRepository;
        this.userChannelRepository = userChannelRepository;
        this.userVideoRepository = userVideoRepository;
        this.videoRepository = videoRepository;
        this.videoTagRepository = videoTagRepository;
    }

    public async Task<Result<AddStandaloneVideoResponse>> AddStandaloneVideoAsync(AddStandaloneVideoRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(request.Url))
            {
                return Result.Invalid([new ValidationError("Url", "A video URL is required")]);
            }

            var provider = metadataProviderFactory.GetProvider(request.Url);
            if (provider is null)
            {
                return Result.Invalid([new ValidationError("Url", "No metadata provider found for this URL")]);
            }

            var videoMeta = await provider.GetVideoMetadataAsync(request.Url, cancellationToken);
            if (videoMeta is null)
            {
                return Result.Invalid([new ValidationError("Url", "Could not retrieve video metadata. Please check the URL and try again.")]);
            }

            var channel = await channelService.GetChannelAsync(videoMeta.Platform, videoMeta.ChannelId);

            if (channel is null)
            {
                ChannelMetadata? channelMeta = null;
                if (!string.IsNullOrEmpty(videoMeta.ChannelId))
                {
                    string channelUrl = provider.BuildChannelUrl(videoMeta.ChannelId);
                    channelMeta = await provider.GetChannelMetadataAsync(channelUrl, cancellationToken);
                }

                channel = await channelRepository.InsertAsync(new Channel
                {
                    ChannelId = videoMeta.ChannelId ?? videoMeta.ChannelName ?? "unknown",
                    Name = channelMeta?.Name ?? videoMeta.ChannelName ?? "Unknown Channel",
                    Url = channelMeta?.Url ?? (string.IsNullOrEmpty(videoMeta.ChannelId) ? string.Empty : provider.BuildChannelUrl(videoMeta.ChannelId)),
                    Description = channelMeta?.Description,
                    BannerUrl = channelMeta?.BannerUrl,
                    SubscriberCount = channelMeta?.SubscriberCount,
                    Platform = videoMeta.Platform,
                    SubscribedAt = DateTime.UtcNow
                });

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Created channel {ChannelId} for standalone video", channel.ChannelId);
                }
            }

            var video = await videoRepository.FindOneAsync(new SearchOptions<Video>
            {
                Query = x => x.VideoId == videoMeta.VideoId && x.Platform == videoMeta.Platform
            });

            if (video is null)
            {
                video = new Video
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
                    ChannelId = channel.Id,
                    IsQueued = true
                };

                await videoRepository.InsertAsync(video);

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Created standalone video {VideoId}", video.VideoId);
                }
            }

            var standaloneTag = await tagService.GetOrCreateTagAsync(userId, Constants.StandaloneTag);

            bool alreadyTagged = await videoTagRepository.ExistsAsync(x => x.VideoId == video.Id && x.TagId == standaloneTag.Id);
            if (!alreadyTagged)
            {
                await videoTagRepository.InsertAsync(new VideoTag
                {
                    VideoId = video.Id,
                    TagId = standaloneTag.Id
                });
            }

            if (video.DownloadedAt is null && !video.DownloadFailed)
            {
                video.IsQueued = true;
                await videoRepository.UpdateAsync(video);
                backgroundJobClient.Enqueue<VideoDownloadJob>(job => job.ExecuteAsync(video.Id, CancellationToken.None));
            }

            return Result.Success(new AddStandaloneVideoResponse(
                video.Id,
                video.Title,
                channel.Id,
                channel.Name,
                video.DownloadedAt is not null));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error adding standalone video from URL {Url}", request.Url);
            }

            return Result.Error("An error occurred while adding the video");
        }
    }

    public async Task<Result> DeleteVideoFileAsync(int channelId, int videoId)
    {
        try
        {
            bool isAdminUser = userContextService.IsAdministrator();

            var video = await videoRepository.FindOneAsync(new SearchOptions<Video>
            {
                Query = x => x.Id == videoId && x.ChannelId == channelId
            });

            if (video is null)
            {
                return Result.NotFound("Video not found");
            }

            string userId = userContextService.GetCurrentUserId()!;

            // Always: clear this user's tags and mark the video as personally ignored.
            await tagService.SetVideoTagsAsync(videoId, new SetVideoTagsRequest());
            await UpsertUserVideoAsync(userId, videoId, watched: false, isIgnored: true);

            if (!isAdminUser)
            {
                // Non-admin: user-level hide is sufficient — the file stays on disk for others.
                await customPlaylistService.RemoveVideoFromAllPlaylistsForUserAsync(videoId, userId);
                return Result.Success();
            }

            // Admin path: physically delete the file only when no other user still has it on a playlist.
            if (await customPlaylistService.VideoAppearsOnAnyPlaylistsForOtherUsers(videoId, userId))
            {
                return Result.Forbidden();
            }

            await customPlaylistService.RemoveVideoFromAllPlaylistsAsync(videoId);

            if (!string.IsNullOrEmpty(video.FilePath) && File.Exists(video.FilePath))
            {
                File.Delete(video.FilePath);
            }

            video.DownloadedAt = null;
            video.FilePath = null;
            video.FileSize = null;
            video.IsIgnored = true;
            await videoRepository.UpdateAsync(video);

            return Result.Success();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error deleting video file for video {VideoId}", videoId);
            }

            return Result.Error("An error occurred while deleting the video file");
        }
    }

    public async Task<Result<GetAccessibleChannelsResponse>> GetAccessibleChannelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            bool isAdmin = userContextService.IsAdministrator();

            if (isAdmin)
            {
                var allChannels = await channelRepository.FindAsync(
                    new SearchOptions<Channel>
                    {
                        OrderBy = q => q.OrderBy(x => x.Name)
                    },
                    x => new ChannelFilterItem(x.Id, x.Name));

                return Result.Success(new GetAccessibleChannelsResponse(allChannels.ToList()));
            }

            var subscribedChannelIds = (await userChannelRepository.FindAsync(
                new SearchOptions<UserChannel>
                {
                    Query = x => x.UserId == userId
                },
                x => x.ChannelId)).ToList();

            var standaloneTag = await tagService.GetStandaloneTagAsync(userId);

            if (standaloneTag is not null)
            {
                var standaloneVideoChannelIds = await videoTagRepository.FindAsync(
                    new SearchOptions<VideoTag>
                    {
                        Query = x => x.TagId == standaloneTag.Id,
                        Include = q => q.Include(x => x.Video)
                    },
                    x => x.Video.ChannelId);

                subscribedChannelIds = subscribedChannelIds
                    .Union(standaloneVideoChannelIds)
                    .Distinct()
                    .ToList();
            }

            if (subscribedChannelIds.Count == 0)
            {
                return Result.Success(new GetAccessibleChannelsResponse([]));
            }

            var channels = await channelRepository.FindAsync(
                new SearchOptions<Channel>
                {
                    Query = x => subscribedChannelIds.Contains(x.Id),
                    OrderBy = q => q.OrderBy(x => x.Name)
                },
                x => new ChannelFilterItem(x.Id, x.Name));

            return Result.Success(new GetAccessibleChannelsResponse(channels.ToList()));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving accessible channels");
            }

            return Result.Error("An error occurred while retrieving channels");
        }
    }

    public async Task<Result<FailedDownloadsResponse>> GetFailedDownloadsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var videos = await videoRepository.FindAsync(
                new SearchOptions<Video>
                {
                    CancellationToken = cancellationToken,
                    Query = x => x.DownloadFailed,
                    OrderBy = q => q.OrderBy(x => x.Channel.Name).ThenBy(x => x.Title),
                    Include = q => q.Include(x => x.Channel)
                });

            var items = videos.Select(x => new FailedDownloadItem(
                x.Id,
                x.VideoId,
                x.Title,
                x.Url,
                x.Platform,
                x.ThumbnailUrl,
                x.Channel.ChannelId,
                x.Channel.Name,
                x.ChannelId)).ToList();

            return Result.Success(new FailedDownloadsResponse(items));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving failed downloads");
            }

            return Result.Error("An error occurred while retrieving failed downloads");
        }
    }

    public async Task<Result<StandaloneInfoResponse>> GetStandaloneInfoAsync(int videoId)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            var video = await videoRepository.FindOneAsync(new SearchOptions<Video>
            {
                Query = x => x.Id == videoId,
                Include = q => q.Include(x => x.Channel)
            });

            if (video is null)
            {
                return Result.NotFound("Video not found");
            }

            var standaloneTag = await tagService.GetStandaloneTagAsync(userId);

            bool isStandalone = false;
            if (standaloneTag is not null)
            {
                isStandalone = await videoTagRepository.ExistsAsync(x => x.VideoId == videoId && x.TagId == standaloneTag.Id);
            }

            var channelVideos = await videoRepository.FindAsync(
                new SearchOptions<Video>
                {
                    Query = x => x.ChannelId == video.ChannelId && x.DownloadedAt != null
                },
                x => x.Id);

            bool isSubscribed = await userChannelRepository.ExistsAsync(
                x => x.UserId == userId && x.ChannelId == video.ChannelId);

            return Result.Success(new StandaloneInfoResponse(
                isStandalone,
                channelVideos.ItemCount,
                video.ChannelId,
                video.Channel.Name,
                video.Channel.Url,
                video.Channel.ChannelId,
                video.Channel.Platform,
                isSubscribed));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving standalone info for video {VideoId}", videoId);
            }

            return Result.Error("An error occurred while retrieving video info");
        }
    }

    public async Task<Result<GetVideoPlaylistsResponse>> GetVideoPlaylistsAsync(int videoId)
    {
        try
        {
            var playlistVideos = await playlistVideoRepository.FindAsync(new SearchOptions<PlaylistVideo>
            {
                Query = x => x.VideoId == videoId,
                Include = query => query.Include(x => x.Playlist)
            });

            var playlists = playlistVideos
                .Select(x => new VideoPlaylistItem(
                    x.Playlist.Id,
                    x.Playlist.Name,
                    x.Playlist.Platform,
                    x.Playlist.Url))
                .ToList();

            return Result.Success(new GetVideoPlaylistsResponse(playlists));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving playlists for video {VideoId}", videoId);
            }

            return Result.Error("An error occurred while retrieving playlists");
        }
    }

    public async Task<Result<VideoIndexPageResponse>> GetVideosAsync(
        int page = 1,
        int pageSize = 60,
        string? search = null,
        int? channelId = null,
        string? tagFilter = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            bool isAdmin = userContextService.IsAdministrator();
            var predicate = PredicateBuilder.New<Video>(false);

            if (isAdmin)
            {
                // Admins see all downloaded videos; Video.IsIgnored is the admin-level global block.
                predicate = PredicateBuilder.New<Video>(x => x.DownloadedAt != null && !x.IsIgnored);
            }
            else
            {
                // Fetch IDs of videos the current user has personally ignored (user-level).
                var userIgnoredVideoIds = (await userVideoRepository.FindAsync(
                    new SearchOptions<UserVideo>
                    {
                        Query = x => x.UserId == userId && x.IsIgnored
                    },
                    x => x.VideoId)).ToHashSet();

                var subscribedChannelIds = (await userChannelRepository.FindAsync(
                    new SearchOptions<UserChannel> { Query = x => x.UserId == userId },
                    x => x.ChannelId)).ToList();

                if (subscribedChannelIds.Count > 0)
                {
                    predicate = predicate.Or(x =>
                        subscribedChannelIds.Contains(x.ChannelId) &&
                        x.DownloadedAt != null &&
                        !x.IsIgnored &&
                        !userIgnoredVideoIds.Contains(x.Id));
                }

                var standaloneTag = await tagService.GetStandaloneTagAsync(userId);

                if (standaloneTag is not null)
                {
                    var standaloneVideoIds = (await videoTagRepository.FindAsync(
                        new SearchOptions<VideoTag> { Query = x => x.TagId == standaloneTag.Id },
                        x => x.VideoId)).ToList();

                    if (standaloneVideoIds.Count > 0)
                    {
                        predicate = predicate.Or(x =>
                            standaloneVideoIds.Contains(x.Id) &&
                            x.DownloadedAt != null &&
                            !x.IsIgnored &&
                            !userIgnoredVideoIds.Contains(x.Id));
                    }
                }

                if (!predicate.IsStarted)
                {
                    return Result.Success(new VideoIndexPageResponse(
                        [],
                        page,
                        pageSize,
                        0,
                        0));
                }
            }

            if (channelId.HasValue)
            {
                predicate = predicate.And(x => x.ChannelId == channelId.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                string searchLower = search.Trim().ToLower();
                predicate = predicate.And(x => x.Title.ToLower().Contains(searchLower));
            }

            if (!string.IsNullOrWhiteSpace(tagFilter))
            {
                string[] tagNames = tagFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (tagNames.Length > 0)
                {
                    var matchingTagIds = (await tagService.GetTagIdsByNameAsync(userId, tagNames)).ToList();
                    if (matchingTagIds.Count > 0)
                    {
                        predicate = predicate.And(x => x.VideoTags.Any(vt => matchingTagIds.Contains(vt.TagId)));
                    }
                    else
                    {
                        return Result.Success(new VideoIndexPageResponse([], page, pageSize, 0, 0));
                    }
                }
            }

            var options = new SearchOptions<Video>
            {
                CancellationToken = cancellationToken,
                Query = predicate,
                OrderBy = q => q.OrderByDescending(x => x.DownloadedAt ?? x.UploadDate),
                PageNumber = page,
                PageSize = pageSize,
                Include = q => q.Include(x => x.Channel)
                    .Include(x => x.VideoTags)
                    .ThenInclude(vt => vt.Tag)
            };

            var pagedVideos = await videoRepository.FindAsync(options);

            var videos = pagedVideos.Select(x => new VideoIndexItem(
                x.Id,
                x.VideoId,
                x.Title,
                x.ThumbnailUrl,
                x.Duration,
                x.UploadDate,
                x.DownloadedAt,
                x.IsQueued,
                x.DownloadFailed,
                x.Platform,
                new ChannelInfo(x.Channel.Id, x.Channel.Name),
                x.VideoTags
                    .Where(vt => vt.Tag.UserId == userId)
                    .Select(vt => vt.Tag.Name)
                    .ToList())).ToList();

            return Result.Success(new VideoIndexPageResponse(
                videos,
                page,
                pageSize,
                pagedVideos.ItemCount,
                pagedVideos.PageCount));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving video index");
            }

            return Result.Error("An error occurred while retrieving videos");
        }
    }

    public async Task<Result<VideoStreamInfo>> GetVideoStreamInfoAsync(int videoId)
    {
        try
        {
            var video = await videoRepository.FindOneAsync(videoId);

            if (video is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Video with ID {VideoId} not found", videoId);
                }

                return Result.NotFound("Video not found");
            }

            if (string.IsNullOrEmpty(video.FilePath) || !File.Exists(video.FilePath))
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Video file not found for video ID {VideoId} at path {FilePath}", videoId, video.FilePath);
                }

                return Result.NotFound("Video file not found");
            }

            string fileExtension = Path.GetExtension(video.FilePath).ToLowerInvariant();
            string contentType = fileExtension switch
            {
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".mkv" => "video/x-matroska",
                ".avi" => "video/x-msvideo",
                ".mov" => "video/quicktime",
                ".flv" => "video/x-flv",
                _ => "application/octet-stream"
            };

            return Result.Success(new VideoStreamInfo(video.FilePath, contentType));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error streaming video {VideoId}", videoId);
            }

            return Result.Error("An error occurred while streaming the video");
        }
    }

    public async Task<Result<GetWatchedVideoIdsResponse>> GetWatchedVideoIdsAsync(int[] videoIds)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            if (videoIds.IsNullOrEmpty())
            {
                return Result.Success(new GetWatchedVideoIdsResponse(Array.Empty<int>()));
            }

            var watchedIds = await userVideoRepository.FindAsync(
                new SearchOptions<UserVideo>
                {
                    Query = x =>
                        x.UserId == userId &&
                        videoIds.Contains(x.VideoId) &&
                        x.Watched
                },
                x => x.VideoId);

            return Result.Success(new GetWatchedVideoIdsResponse(watchedIds.ToList()));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving watched video IDs");
            }

            return Result.Error("An error occurred while retrieving watched status");
        }
    }

    public async Task<Result<GetWatchedVideoIdsResponse>> GetWatchedVideoIdsByChannelAsync(int channelId)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            bool canAccessChannel = userContextService.IsAdministrator()
                                    || await channelService.UserSubscribedToChannelAsync(channelId);
            if (!canAccessChannel)
            {
                return Result.Forbidden();
            }

            var channelVideoIds = await videoRepository.FindAsync(
                new SearchOptions<Video>
                {
                    Query = x => x.ChannelId == channelId
                },
                x => x.Id);

            var videoIds = channelVideoIds.ToList();
            if (videoIds.Count == 0)
            {
                return Result.Success(new GetWatchedVideoIdsResponse([]));
            }

            var watchedIds = await userVideoRepository.FindAsync(
                new SearchOptions<UserVideo>
                {
                    Query = x =>
                        x.UserId == userId &&
                        videoIds.Contains(x.VideoId) &&
                        x.Watched
                },
                x => x.VideoId);

            return Result.Success(new GetWatchedVideoIdsResponse(watchedIds.ToList()));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving watched video IDs for channel {ChannelId}", channelId);
            }

            return Result.Error("An error occurred while retrieving watched status");
        }
    }

    public async Task<Result<GetWatchedVideoIdsResponse>> GetWatchedVideoIdsByPlaylistAsync(int playlistId)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            var playlistVideoIds = await playlistVideoRepository.FindAsync(
                new SearchOptions<PlaylistVideo>
                {
                    Query = x => x.PlaylistId == playlistId
                },
                x => x.VideoId);

            var videoIds = playlistVideoIds.ToList();
            if (videoIds.Count == 0)
            {
                return Result.Success(new GetWatchedVideoIdsResponse([]));
            }

            var watchedIds = await userVideoRepository.FindAsync(
                new SearchOptions<UserVideo>
                {
                    Query = x =>
                        x.UserId == userId &&
                        videoIds.Contains(x.VideoId) &&
                        x.Watched
                },
                x => x.VideoId);

            return Result.Success(new GetWatchedVideoIdsResponse(watchedIds.ToList()));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving watched video IDs for playlist {PlaylistId}", playlistId);
            }

            return Result.Error("An error occurred while retrieving watched status");
        }
    }

    public async Task<Result> MarkUnwatchedAsync(int videoId)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            await UpsertUserVideoAsync(userId, videoId, watched: false);

            return Result.Success();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error marking video {VideoId} as unwatched", videoId);
            }

            return Result.Error("An error occurred while updating watch status");
        }
    }

    public async Task<Result> MarkWatchedAsync(int videoId)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            await UpsertUserVideoAsync(userId, videoId, watched: true);

            return Result.Success();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error marking video {VideoId} as watched", videoId);
            }

            return Result.Error("An error occurred while updating watch status");
        }
    }

    public async Task<Result<RetryMetadataResponse>> RetryMetadataAsync(int videoId, CancellationToken cancellationToken = default)
    {
        try
        {
            var video = await videoRepository.FindOneAsync(new SearchOptions<Video>
            {
                CancellationToken = cancellationToken,
                Query = x => x.Id == videoId,
                Include = query => query.Include(x => x.Channel)
            });

            if (video is null)
            {
                return Result.NotFound("Video not found");
            }

            var provider = metadataProviderFactory.GetProviderByPlatform(video.Platform);
            if (provider is null)
            {
                return Result.Invalid([new ValidationError("Platform", $"No metadata provider for platform '{video.Platform}'")]);
            }

            var metadata = await provider.GetVideoMetadataAsync(video.VideoId, cancellationToken);
            if (metadata is null || metadata.Title == Constants.PrivateVideoTitle)
            {
                return Result.Success(new RetryMetadataResponse(false, "Metadata still unavailable from platform"));
            }

            if (metadata.Title == Constants.DeletedVideoTitle)
            {
                video.NeedsMetadataReview = false;
                await videoRepository.UpdateAsync(video, ContextOptions.ForCancellationToken(cancellationToken));
                return Result.Success(new RetryMetadataResponse(false, "Video was deleted from platform"));
            }

            video.Title = metadata.Title;
            video.Description = metadata.Description;
            video.ThumbnailUrl = metadata.ThumbnailUrl;
            video.Duration = metadata.Duration;
            video.UploadDate = metadata.UploadDate;
            video.ViewCount = metadata.ViewCount;
            video.LikeCount = metadata.LikeCount;
            video.Url = metadata.Url;
            video.NeedsMetadataReview = false;
            await videoRepository.UpdateAsync(video, ContextOptions.ForCancellationToken(cancellationToken));

            logger.LogInformation("Successfully fetched metadata for video {VideoId}", videoId);

            return Result.Success(new RetryMetadataResponse(true, "Metadata retrieved successfully"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrying metadata for video {VideoId}", videoId);
            return Result.Error("An error occurred while retrying metadata");
        }
    }

    public async Task<Result<bool>> ToggleIgnoreAsync(int channelId, int videoId, IgnoreVideoRequest request)
    {
        try
        {
            if (!await channelService.UserSubscribedToChannelAsync(channelId))
            {
                return Result.Forbidden();
            }

            string userId = userContextService.GetCurrentUserId()!;
            bool isAdmin = userContextService.IsAdministrator();

            if (isAdmin)
            {
                // Admin: global block — affects all users.
                var video = await videoRepository.FindOneAsync(new SearchOptions<Video>
                {
                    Query = x => x.Id == videoId && x.ChannelId == channelId
                });

                if (video is null)
                {
                    return Result.NotFound("Video not found");
                }

                video.IsIgnored = request.IsIgnored;
                await videoRepository.UpdateAsync(video);
                return Result.Success(video.IsIgnored);
            }
            else
            {
                // Non-admin: personal hide — only affects this user.
                bool videoExists = await videoRepository.ExistsAsync(
                    x => x.Id == videoId && x.ChannelId == channelId);

                if (!videoExists)
                {
                    return Result.NotFound("Video not found");
                }

                await UpsertUserVideoAsync(userId, videoId, watched: false, isIgnored: request.IsIgnored);
                return Result.Success(request.IsIgnored);
            }
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error toggling ignore status for video {VideoId}", videoId);
            }

            return Result.Error("An error occurred while updating video status");
        }
    }

    private async Task UpsertUserVideoAsync(string userId, int videoId, bool watched, bool? isIgnored = null)
    {
        var existing = await userVideoRepository.FindOneAsync(new SearchOptions<UserVideo>
        {
            Query = x => x.UserId == userId && x.VideoId == videoId
        });

        if (existing is not null)
        {
            existing.Watched = watched;
            if (isIgnored.HasValue) existing.IsIgnored = isIgnored.Value;
            await userVideoRepository.UpdateAsync(existing);
        }
        else
        {
            await userVideoRepository.InsertAsync(new UserVideo
            {
                UserId = userId,
                VideoId = videoId,
                Watched = watched,
                IsIgnored = isIgnored ?? false
            });
        }
    }
}