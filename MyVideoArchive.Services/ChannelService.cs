using Ardalis.Result;
using Extenso.Collections.Generic;
using Hangfire;
using Hangfire.Common;
using MyVideoArchive.Models.Requests;
using MyVideoArchive.Models.Responses;
using static Dapper.SqlMapper;

namespace MyVideoArchive.Services;

public class ChannelService : IChannelService
{
    private readonly ILogger<ChannelService> logger;
    private readonly IConfiguration configuration;
    private readonly IBackgroundJobClient backgroundJobClient;
    private readonly IUserContextService userContextService;
    private readonly IUserInfoService userInfoService;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<ChannelTag> channelTagRepository;
    private readonly IRepository<CustomPlaylistVideo> customPlaylistVideoRepository;
    private readonly IRepository<Playlist> playlistRepository;
    private readonly IRepository<PlaylistTag> playlistTagRepository;
    private readonly IRepository<PlaylistVideo> playlistVideoRepository;
    private readonly IRepository<UserChannel> userChannelRepository;
    private readonly IRepository<UserPlaylist> userPlaylistRepository;
    private readonly IRepository<UserPlaylistVideo> userPlaylistVideoRepository;
    private readonly IRepository<UserVideo> userVideoRepository;
    private readonly IRepository<Video> videoRepository;
    private readonly IRepository<VideoTag> videoTagRepository;

    public ChannelService(
        ILogger<ChannelService> logger,
        IConfiguration configuration,
        IBackgroundJobClient backgroundJobClient,
        IUserContextService userContextService,
        IUserInfoService userInfoService,
        IRepository<Channel> channelRepository,
        IRepository<ChannelTag> channelTagRepository,
        IRepository<CustomPlaylistVideo> customPlaylistVideoRepository,
        IRepository<Playlist> playlistRepository,
        IRepository<PlaylistTag> playlistTagRepository,
        IRepository<PlaylistVideo> playlistVideoRepository,
        IRepository<UserChannel> userChannelRepository,
        IRepository<UserPlaylist> userPlaylistRepository,
        IRepository<UserPlaylistVideo> userPlaylistVideoRepository,
        IRepository<UserVideo> userVideoRepository,
        IRepository<Video> videoRepository,
        IRepository<VideoTag> videoTagRepository)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.backgroundJobClient = backgroundJobClient;
        this.userInfoService = userInfoService;
        this.channelRepository = channelRepository;
        this.channelTagRepository = channelTagRepository;
        this.customPlaylistVideoRepository = customPlaylistVideoRepository;
        this.playlistRepository = playlistRepository;
        this.playlistTagRepository = playlistTagRepository;
        this.playlistVideoRepository = playlistVideoRepository;
        this.userChannelRepository = userChannelRepository;
        this.userPlaylistRepository = userPlaylistRepository;
        this.userPlaylistVideoRepository = userPlaylistVideoRepository;
        this.userVideoRepository = userVideoRepository;
        this.videoRepository = videoRepository;
        this.videoTagRepository = videoTagRepository;
        this.userContextService = userContextService;
    }

    public async Task<Result> DeleteChannelAsync(int id, bool deleteMetadata = false, bool deleteFiles = false)
    {
        try
        {
            var channel = await channelRepository.FindOneAsync(id);
            if (channel is null)
            {
                return Result.NotFound("Channel not found");
            }

            // Always: remove all user subscriptions & related playlist/video associations for the channel
            await customPlaylistVideoRepository.DeleteAsync(x => x.Video.ChannelId == id);
            await userPlaylistVideoRepository.DeleteAsync(x => x.Video.ChannelId == id);
            await userPlaylistRepository.DeleteAsync(x => x.Playlist.ChannelId == id);
            await userVideoRepository.DeleteAsync(x => x.Video.ChannelId == id);

            int subscriptionCount = await userChannelRepository.DeleteAsync(x => x.ChannelId == id);

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Admin removed {Count} subscription(s) from channel {ChannelId}",
                    subscriptionCount, id);
            }

            if (deleteFiles)
            {
                string downloadPath = configuration.GetValue<string>("VideoDownload:OutputPath")
                    ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

                string channelPath = Path.Combine(downloadPath, channel.ChannelId);

                int deletedFileCount = 0;

                if (!string.IsNullOrEmpty(channelPath) && Directory.Exists(channelPath))
                {
                    var files = Directory.EnumerateFiles(channelPath, "*", SearchOption.AllDirectories).ToList();
                    deletedFileCount = files.Count;

                    foreach (string file in files)
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }

                    Directory.Delete(channelPath, recursive: true);

                    if (logger.IsEnabled(LogLevel.Information))
                    {
                        logger.LogInformation("Admin deleted {Count} video file(s) for channel {ChannelId}",
                            deletedFileCount, id);
                    }
                }

                if (!deleteMetadata)
                {
                    await videoRepository.UpdateAsync(
                        x => x.ChannelId == id && x.FilePath != null,
                        setters => setters
                            .SetProperty(p => p.FilePath, (string?)null)
                            .SetProperty(p => p.FileSize, (long?)null)
                            .SetProperty(p => p.DownloadedAt, (DateTime?)null));
                }
            }

            if (deleteMetadata)
            {
                await videoTagRepository.DeleteAsync(x => x.Video.ChannelId == id);
                await playlistVideoRepository.DeleteAsync(x => x.Video.ChannelId == id);
                await videoRepository.DeleteAsync(x => x.ChannelId == id);
                await playlistTagRepository.DeleteAsync(x => x.Playlist.ChannelId == id);
                await playlistRepository.DeleteAsync(x => x.ChannelId == id);
                await channelTagRepository.DeleteAsync(x => x.ChannelId == id);
                await channelRepository.DeleteAsync(channel);

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Admin deleted channel {ChannelId} and all associated metadata", id);
                }
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error performing admin delete for channel {ChannelId}", id);
            }

            return Result.Error("An error occurred while deleting the channel");
        }
    }

    public async Task<Result<int>> DownloadAllVideosAsync(int channelId)
    {
        try
        {
            // Check user access
            if (!await UserSubscribedToChannelAsync(channelId))
            {
                return Result.Forbidden();
            }

            var videos = await videoRepository.FindAsync(new SearchOptions<Video>
            {
                Query = x =>
                    x.ChannelId == channelId &&
                    x.DownloadedAt == null &&
                    !x.IsIgnored &&
                    !x.IsQueued &&
                    !x.DownloadFailed
            });

            if (videos.Count == 0)
            {
                return Result.Success(0);
            }

            var videoUpdates = new List<Video>();
            foreach (var video in videos)
            {
                video.IsQueued = true;
                videoUpdates.Add(video);

                backgroundJobClient.Enqueue<VideoDownloadJob>(job =>
                    job.ExecuteAsync(video.Id, CancellationToken.None));
            }

            await videoRepository.UpdateAsync(videoUpdates);

            return Result.Success(videos.Count);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error queueing all video downloads for channel {ChannelId}", channelId);
            }

            return Result.Error("An error occurred while queueing downloads");
        }
    }

    public async Task<Result<int>> DownloadVideosAsync(int channelId, DownloadVideosRequest request)
    {
        try
        {
            // Check user access
            if (!await UserSubscribedToChannelAsync(channelId))
            {
                return Result.Forbidden();
            }

            if (request.VideoIds.IsNullOrEmpty())
            {
                return Result.Invalid([new ValidationError("No video IDs provided")]);
            }

            var videos = await videoRepository.FindAsync(new SearchOptions<Video>
            {
                Query = x =>
                    x.ChannelId == channelId &&
                    request.VideoIds.Contains(x.Id) &&
                    !x.DownloadFailed
            });

            if (videos.Count == 0)
            {
                return Result.NotFound("No videos found");
            }

            var videoUpdates = new List<Video>();
            int queuedCount = 0;
            foreach (var video in videos)
            {
                // Only queue if not already downloaded and not already queued
                if (video.DownloadedAt is null && !video.IsQueued)
                {
                    video.IsQueued = true;
                    videoUpdates.Add(video);

                    backgroundJobClient.Enqueue<VideoDownloadJob>(job =>
                        job.ExecuteAsync(video.Id, CancellationToken.None));
                    queuedCount++;
                }
            }

            await videoRepository.UpdateAsync(videoUpdates);

            return Result.Success(queuedCount);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error queueing video downloads for channel {ChannelId}", channelId);
            }

            return Result.Error("An error occurred while queueing downloads");
        }
    }

    public async Task<Result<IPagedCollection<AvailableVideo>>> GetAvailableVideosAsync(
        int channelId,
        int page = 1,
        int pageSize = 20,
        bool showIgnored = false,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check user access
            if (!await UserSubscribedToChannelAsync(channelId))
            {
                return Result.Forbidden();
            }

            var channel = await channelRepository.FindOneAsync(channelId);
            if (channel is null)
            {
                return Result.NotFound("Channel not found");
            }

            var predicate = PredicateBuilder.New<Video>(x => x.ChannelId == channelId);

            // Only show videos that haven't been downloaded yet and aren't queued
            predicate = predicate.And(x => x.DownloadedAt == null && !x.IsQueued);

            // Exclude private videos
            predicate = predicate.And(x => x.Title != Constants.PrivateVideoTitle);

            // Filter based on showIgnored flag.
            // Video.IsIgnored is the admin-level global block; UserVideo.IsIgnored is per-user.
            bool isAdmin = userContextService.IsAdministrator();
            string? currentUserId = userContextService.GetCurrentUserId();

            if (!showIgnored)
            {
                predicate = predicate.And(x => !x.IsIgnored);

                if (!isAdmin && !string.IsNullOrEmpty(currentUserId))
                {
                    var userIgnoredVideoIds = (await userVideoRepository.FindAsync(
                        new SearchOptions<UserVideo>
                        {
                            Query = x => x.UserId == currentUserId && x.IsIgnored
                        },
                        x => x.VideoId)).ToHashSet();

                    if (userIgnoredVideoIds.Count > 0)
                    {
                        predicate = predicate.And(x => !userIgnoredVideoIds.Contains(x.Id));
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                string searchLower = search.Trim().ToLower();
                predicate = predicate.And(x => x.Title.ToLower().Contains(searchLower));
            }

            // Apply pagination
            var videos = await videoRepository.FindAsync(new SearchOptions<Video>
            {
                CancellationToken = cancellationToken,
                Query = predicate,
                OrderBy = showIgnored
                    ? query => query
                        .OrderBy(x => x.IsIgnored) // ignroed videos last
                        .ThenByDescending(x => x.UploadDate)
                    : query => query
                        .OrderByDescending(x => x.UploadDate),
                PageNumber = page,
                PageSize = pageSize
            }, x => new AvailableVideo(x.Id,
                x.VideoId,
                x.Title,
                x.Description,
                x.Url,
                x.ThumbnailUrl,
                x.Duration,
                x.UploadDate,
                x.ViewCount,
                x.LikeCount,
                x.DownloadedAt,
                x.IsIgnored, // admin-level flag; UI still uses this for the Ignored badge
                x.DownloadedAt != null,
                x.DownloadFailed));

            return Result.Success(videos);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving available videos for channel {ChannelId}", channelId);
            }

            return Result.Error("An error occurred while retrieving videos");
        }
    }

    public async Task<Channel> GetChannelAsync(string platformName, string channelId) =>
        await channelRepository.FindOneAsync(new SearchOptions<Channel>
        {
            Query = x => x.ChannelId == channelId && x.Platform == platformName
        });

    public async Task<Result<IPagedCollection<ChannelSubscriberResponse>>> GetChannelSubscribersAsync(int id, CancellationToken cancellationToken = default)
    {
        bool channelExists = await channelRepository.ExistsAsync(
            x => x.Id == id,
            ContextOptions.ForCancellationToken(cancellationToken));

        if (!channelExists)
        {
            return Result.NotFound("Channel not found.");
        }

        var userChannels = await userChannelRepository.FindAsync(new SearchOptions<UserChannel>
        {
            CancellationToken = cancellationToken,
            Query = x => x.ChannelId == id,
            OrderBy = query => query.OrderBy(x => x.SubscribedAt)
        });

        var userInfoMap = await userInfoService.GetUserInfoAsync(
            userChannels.Select(x => x.UserId),
            cancellationToken);

        var subscribers = userChannels
            .Select(x =>
            {
                userInfoMap.TryGetValue(x.UserId, out var info);
                return new ChannelSubscriberResponse(
                    x.UserId,
                    info?.Username ?? x.UserId,
                    info?.Email ?? string.Empty,
                    x.SubscribedAt);
            });

        IPagedCollection<ChannelSubscriberResponse> results = new PagedList<ChannelSubscriberResponse>(
            subscribers,
            userChannels.PageIndex,
            userChannels.PageSize,
            userChannels.ItemCount);

        return Result.Success(results);
    }

    public async Task<Result<IPagedCollection<DownloadedVideo>>> GetDownloadedVideosAsync(
        int channelId,
        int page = 1,
        int pageSize = 24,
        string? search = null,
        int? playlistId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await UserSubscribedToChannelAsync(channelId))
            {
                return Result.Forbidden();
            }

            bool channelExists = await channelRepository.ExistsAsync(x => x.Id == channelId);
            if (!channelExists)
            {
                return Result.NotFound("Channel not found");
            }

            var predicate = PredicateBuilder.New<Video>(x => x.ChannelId == channelId && x.DownloadedAt != null);

            if (!string.IsNullOrWhiteSpace(search))
            {
                string searchLower = search.Trim().ToLower();
                predicate = predicate.And(x => x.Title.ToLower().Contains(searchLower));
            }

            if (playlistId.HasValue)
            {
                if (playlistId.Value == -1)
                {
                    // Videos not in any playlist
                    predicate = predicate.And(x => !x.PlaylistVideos.Any());
                }
                else if (playlistId.Value > 0)
                {
                    predicate = predicate.And(x => x.PlaylistVideos.Any(pv => pv.PlaylistId == playlistId.Value));
                }
            }

            var videos = await videoRepository.FindAsync(new SearchOptions<Video>
            {
                CancellationToken = cancellationToken,
                Query = predicate,
                OrderBy = query => query.OrderByDescending(x => x.UploadDate),
                PageNumber = page,
                PageSize = pageSize
            }, x => new DownloadedVideo(x.Id,
                x.VideoId,
                x.Title,
                x.Url,
                x.ThumbnailUrl,
                x.Duration,
                x.UploadDate,
                x.DownloadedAt));

            return Result.Success(videos);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving downloaded videos for channel {ChannelId}", channelId);
            }

            return Result.Error("An error occurred while retrieving videos");
        }
    }

    public async Task<bool?> GetSyncStatusAsync(int channelId, CancellationToken cancellationToken = default)
    {
        var channel = await channelRepository.FindOneAsync(new SearchOptions<Channel>
        {
            CancellationToken = cancellationToken,
            Query = x => x.Id == channelId
        });

        if (channel is null)
        {
            return null;
        }

        // Custom channels do not have sync jobs
        if (channel.Platform is "Custom")
        {
            return false;
        }

        // Check Hangfire for a channel sync job currently processing or enqueued for this channel
        try
        {
            var monitoring = JobStorage.Current.GetMonitoringApi();
            const int maxJobs = 500;

            bool IsChannelSyncJobForThisChannel(Job? job)
            {
                return job != null &&
                    job.Type == typeof(ChannelSyncJob) &&
                    string.Equals(job.Method.Name, nameof(ChannelSyncJob.ExecuteAsync), StringComparison.Ordinal) &&
                    job.Args is { Count: > 0 } &&
                    job.Args[0] is int id &&
                    id == channelId;
            }

            // Processing jobs (currently running)
            var processing = monitoring.ProcessingJobs(0, maxJobs);
            if (processing.Any(entry => IsChannelSyncJobForThisChannel(entry.Value?.Job)))
            {
                return true;
            }

            // Enqueued jobs (channel sync uses default queue)
            var enqueued = monitoring.EnqueuedJobs("default", 0, maxJobs);
            return enqueued.Any(entry => IsChannelSyncJobForThisChannel(entry.Value?.Job));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(ex, "Failed to check Hangfire for channel sync status");
            }

            return false;
        }
    }

    public Result SyncAllChannels()
    {
        try
        {
            backgroundJobClient.Enqueue<ChannelSyncJob>(job =>
                job.SyncAllChannelsAsync(CancellationToken.None));

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Queued sync job for all channels");
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error queueing sync job for all channels");
            }

            return Result.Error("An error occurred while queueing sync job for all channels");
        }
    }

    public async Task<Result<IReadOnlyList<ChannelUserSubscriptionStatus>>> GetUserSubscriptionsAsync(
        int channelId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            bool channelExists = await channelRepository.ExistsAsync(
                x => x.Id == channelId,
                ContextOptions.ForCancellationToken(cancellationToken));

            if (!channelExists)
            {
                return Result.NotFound("Channel not found.");
            }

            var allUsers = await userInfoService.GetAllUsersAsync(cancellationToken);

            var subscribedUserIds = (await userChannelRepository.FindAsync(new SearchOptions<UserChannel>
            {
                CancellationToken = cancellationToken,
                Query = x => x.ChannelId == channelId
            }, x => x.UserId)).ToHashSet();

            IReadOnlyList<ChannelUserSubscriptionStatus> result = allUsers
                .Select(u => new ChannelUserSubscriptionStatus(
                    u.UserId,
                    u.Username,
                    u.Email,
                    subscribedUserIds.Contains(u.UserId)))
                .OrderBy(u => u.Username)
                .ToList();

            return Result.Success(result);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(ex, "Error getting user subscriptions for channel {ChannelId}", channelId);

            return Result.Error("An error occurred while retrieving user subscriptions");
        }
    }

    public async Task<Result> UpdateUserSubscriptionsAsync(
        int channelId,
        IEnumerable<string> subscribedUserIds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var channel = await channelRepository.FindOneAsync(channelId);
            if (channel is null)
            {
                return Result.NotFound("Channel not found.");
            }

            var desiredIds = subscribedUserIds.ToHashSet();

            var existing = await userChannelRepository.FindAsync(new SearchOptions<UserChannel>
            {
                CancellationToken = cancellationToken,
                Query = x => x.ChannelId == channelId
            });

            var existingIds = existing.Select(x => x.UserId).ToHashSet();

            // Add new subscriptions
            var toAdd = desiredIds.Except(existingIds).ToList();
            foreach (string userId in toAdd)
            {
                await userChannelRepository.InsertAsync(new UserChannel
                {
                    UserId = userId,
                    ChannelId = channelId,
                    SubscribedAt = DateTime.UtcNow
                });
            }

            // Remove subscriptions not in the desired set
            var toRemove = existing.Where(x => !desiredIds.Contains(x.UserId)).ToList();
            foreach (var record in toRemove)
            {
                await userChannelRepository.DeleteAsync(record);
            }

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Admin updated subscriptions for channel {ChannelId}: +{Added} / -{Removed}",
                    channelId, toAdd.Count, toRemove.Count);
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(ex, "Error updating user subscriptions for channel {ChannelId}", channelId);

            return Result.Error("An error occurred while updating user subscriptions");
        }
    }

    public async Task<bool> UserSubscribedToChannelAsync(int channelId)
    {
        if (userContextService.IsAdministrator())
        {
            return true;
        }

        string? userId = userContextService.GetCurrentUserId();

        return !string.IsNullOrEmpty(userId) &&
            await userChannelRepository.ExistsAsync(x =>
                x.UserId == userId &&
                x.ChannelId == channelId);
    }
}