using Ardalis.Result;
using Extenso.Collections.Generic;
using Hangfire;
using MyVideoArchive.Models.Api;

namespace MyVideoArchive.Services;

public class ChannelService : IChannelService
{
    private readonly ILogger<ChannelService> logger;
    private readonly IConfiguration configuration;
    private readonly IBackgroundJobClient backgroundJobClient;
    private readonly IUserContextService userContextService;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<CustomPlaylistVideo> customPlaylistVideoRepository;
    private readonly IRepository<Playlist> playlistRepository;
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
        IRepository<Channel> channelRepository,
        IRepository<CustomPlaylistVideo> customPlaylistVideoRepository,
        IRepository<Playlist> playlistRepository,
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
        this.channelRepository = channelRepository;
        this.customPlaylistVideoRepository = customPlaylistVideoRepository;
        this.playlistRepository = playlistRepository;
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
                int deletedFileCount = 0;

                string downloadPath = configuration.GetValue<string>("VideoDownload:OutputPath")
                    ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

                string channelPath = Path.Combine(downloadPath, channel.ChannelId);

                if (!string.IsNullOrEmpty(channelPath) && Directory.Exists(channelPath))
                {
                    string[] files = Directory.GetFiles(channelPath);

                    foreach (string file in files)
                    {
                        System.IO.File.SetAttributes(file, FileAttributes.Normal); // handle read-only files
                        System.IO.File.Delete(file);
                        deletedFileCount++;
                    }

                    Directory.Delete(channelPath);
                }

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Admin deleted {Count} video file(s) for channel {ChannelId}",
                        deletedFileCount, id);
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
                await playlistRepository.DeleteAsync(x => x.ChannelId == id);
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
                    request.VideoIds.Contains(x.Id)
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
                    !x.IsQueued
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

            // Filter based on showIgnored flag
            if (!showIgnored)
            {
                predicate = predicate.And(x => !x.IsIgnored);
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
                OrderBy = query => query.OrderByDescending(x => x.UploadDate),
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
                x.IsIgnored,
                x.DownloadedAt != null));

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

public record AvailableVideo(
    int Id,
    string VideoId,
    string Title,
    string? Description,
    string Url,
    string? ThumbnailUrl,
    TimeSpan? Duration,
    DateTime? UploadDate,
    int? ViewCount,
    int? LikeCount,
    DateTime? DownloadedAt,
    bool IsIgnored,
    bool IsDownloaded);

public record DownloadedVideo(
    int Id,
    string VideoId,
    string Title,
    string Url,
    string? ThumbnailUrl,
    TimeSpan? Duration,
    DateTime? UploadDate,
    DateTime? DownloadedAt);