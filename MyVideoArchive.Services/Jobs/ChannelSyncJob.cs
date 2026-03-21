using Hangfire;
using MyVideoArchive.Infrastructure;

namespace MyVideoArchive.Services.Jobs;

/// <summary>
/// Hangfire job for synchronizing channels with their source platforms
/// </summary>
public class ChannelSyncJob
{
    private readonly ILogger<ChannelSyncJob> logger;
    private readonly IConfiguration configuration;
    private readonly VideoMetadataProviderFactory metadataProviderFactory;
    private readonly ThumbnailService thumbnailService;
    private readonly IBackgroundJobClient backgroundJobClient;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<Video> videoRepository;

    public ChannelSyncJob(
        ILogger<ChannelSyncJob> logger,
        IConfiguration configuration,
        VideoMetadataProviderFactory metadataProviderFactory,
        ThumbnailService thumbnailService,
        IBackgroundJobClient backgroundJobClient,
        IRepository<Channel> channelRepository,
        IRepository<Video> videoRepository)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.metadataProviderFactory = metadataProviderFactory;
        this.thumbnailService = thumbnailService;
        this.backgroundJobClient = backgroundJobClient;
        this.channelRepository = channelRepository;
        this.videoRepository = videoRepository;
    }

    [HangfireSkipWhenPreviousInstanceIsRunningFilter]
    public async Task ExecuteAsync(int channelId, CancellationToken cancellationToken = default)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Starting channel sync job for channel ID: {ChannelId}", channelId);
        }

        try
        {
            var channel = await channelRepository.FindOneAsync(new SearchOptions<Channel>
            {
                CancellationToken = cancellationToken,
                Query = x => x.Id == channelId,
                Include = query => query.Include(x => x.Videos)
            });

            if (channel is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Channel with ID {ChannelId} not found", channelId);
                }

                return;
            }

            // Get the appropriate metadata provider
            var provider = metadataProviderFactory.GetProviderByPlatform(channel.Platform);
            if (provider is null)
            {
                if (logger.IsEnabled(LogLevel.Error))
                {
                    logger.LogError("No metadata provider found for platform: {Platform}", channel.Platform);
                }

                return;
            }

            string downloadPath = configuration.GetValue<string>("VideoDownload:OutputPath")
                ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

            // Update channel metadata
            var channelMetadata = await provider.GetChannelMetadataAsync(channel.Url, cancellationToken);
            if (channelMetadata is not null)
            {
                channel.ChannelId = channelMetadata.ChannelId;
                channel.Name = channelMetadata.Name;
                channel.Description = channelMetadata.Description;
                channel.SubscriberCount = channelMetadata.SubscriberCount;

                string channelDir = Path.Combine(downloadPath, channel.ChannelId);

                // Don't overwrite BannerUrl if the user has already uploaded a custom image (served via /api/)
                bool bannerIsUserUploaded = channel.BannerUrl?.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) == true;
                if (!bannerIsUserUploaded)
                {
                    string? localBannerUrl = await thumbnailService.DownloadAndSaveAsync(
                        channelMetadata.BannerUrl, channelDir, "banner", downloadPath, cancellationToken);
                    channel.BannerUrl = localBannerUrl ?? channelMetadata.BannerUrl ?? channel.BannerUrl;
                }

                // Don't overwrite AvatarUrl if the user has already uploaded a custom image (served via /api/)
                bool avatarIsUserUploaded = channel.AvatarUrl?.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) == true;
                if (!avatarIsUserUploaded && channelMetadata.AvatarUrl != null)
                {
                    string? localAvatarUrl = await thumbnailService.DownloadAndSaveAsync(
                        channelMetadata.AvatarUrl, channelDir, "avatar", downloadPath, cancellationToken);
                    channel.AvatarUrl = localAvatarUrl ?? channelMetadata.AvatarUrl;
                }
            }

            // Get all videos from the channel
            var videoMetadataList = await provider.GetChannelVideosAsync(channel.Url, cancellationToken);
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Found {Count} videos for channel {ChannelId}", videoMetadataList.Count, channelId);
            }

            // Process each video — thumbnails are NOT downloaded here.
            // Videos store the original remote URL until the video file is actually downloaded,
            // at which point VideoDownloadJob saves the thumbnail to disk and updates the URL.
            var existingVideoIds = channel.Videos.Select(v => v.VideoId).ToHashSet();
            int newVideosCount = 0;

            var videoUpdates = new List<Video>();
            var videoInserts = new List<Video>();
            foreach (var videoMetadata in videoMetadataList)
            {
                if (existingVideoIds.Contains(videoMetadata.VideoId))
                {
                    // We don't want to overwrite metadata for videos that have been deleted or made private.
                    if (videoMetadata.Title is not Constants.DeletedVideoTitle and not Constants.PrivateVideoTitle)
                    {
                        var existingVideo = channel.Videos.First(v => v.VideoId == videoMetadata.VideoId);
                        existingVideo.Title = videoMetadata.Title;
                        existingVideo.Description = videoMetadata.Description;
                        existingVideo.Duration = videoMetadata.Duration;
                        existingVideo.ViewCount = videoMetadata.ViewCount;
                        existingVideo.LikeCount = videoMetadata.LikeCount;

                        // Keep the original remote URL for videos not yet downloaded.
                        // If the video has already been downloaded the thumbnail points to a local
                        // /archive/… file and must not be overwritten with a remote URL.
                        if (!ThumbnailService.IsLocalUrl(existingVideo.ThumbnailUrl))
                        {
                            existingVideo.ThumbnailUrl = videoMetadata.ThumbnailUrl;
                        }

                        videoUpdates.Add(existingVideo);
                    }
                }
                else
                {
                    // New video — store the original remote thumbnail URL.
                    videoInserts.Add(new Video
                    {
                        VideoId = videoMetadata.VideoId,
                        Title = videoMetadata.Title,
                        Description = videoMetadata.Description,
                        Url = videoMetadata.Url,
                        ThumbnailUrl = videoMetadata.ThumbnailUrl,
                        Platform = videoMetadata.Platform,
                        Duration = videoMetadata.Duration,
                        UploadDate = videoMetadata.UploadDate,
                        ViewCount = videoMetadata.ViewCount,
                        LikeCount = videoMetadata.LikeCount,
                        ChannelId = channelId,
                        IsIgnored = false,
                        NeedsMetadataReview = videoMetadata.Title == Constants.PrivateVideoTitle
                    });
                    newVideosCount++;
                }
            }

            if (!videoInserts.IsNullOrEmpty())
            {
                await videoRepository.InsertAsync(videoInserts, ContextOptions.ForCancellationToken(cancellationToken));
            }

            if (!videoUpdates.IsNullOrEmpty())
            {
                await videoRepository.UpdateAsync(videoUpdates, ContextOptions.ForCancellationToken(cancellationToken));
            }

            // Update last checked timestamp
            channel.LastChecked = DateTime.UtcNow;

            channel.VideoCount = await videoRepository.CountAsync(
                x => x.Id == channelId,
                ContextOptions.ForCancellationToken(cancellationToken));

            await channelRepository.UpdateAsync(channel, ContextOptions.ForCancellationToken(cancellationToken));

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                "Channel sync completed for {ChannelId}. New videos: {NewCount}, Total videos: {TotalCount}",
                channelId, newVideosCount, videoMetadataList.Count);
            }
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error syncing channel {ChannelId}", channelId);
            }

            throw;
        }
    }

    [HangfireSkipWhenPreviousInstanceIsRunningFilter]
    public async Task SyncAllChannelsAsync(CancellationToken cancellationToken = default)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Starting sync for all channels");
        }

        try
        {
            var channelIds = await channelRepository.FindAsync(new SearchOptions<Channel>
            {
                CancellationToken = cancellationToken
            }, x => x.Id);

            foreach (int channelId in channelIds)
            {
                // Queue individual channel sync jobs
                backgroundJobClient.Enqueue<ChannelSyncJob>(job =>
                    job.ExecuteAsync(channelId, CancellationToken.None));
            }
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Queued sync jobs for {Count} channels", channelIds.Count);
            }
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error queuing channel sync jobs");
            }

            throw;
        }
    }

    [HangfireSkipWhenPreviousInstanceIsRunningFilter]
    public async Task SyncChannelAsync(int channelId, CancellationToken cancellationToken = default)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Starting sync for channel: {ChannelId}", channelId);
        }

        try
        {
            // Queue individual channel sync jobs
            backgroundJobClient.Enqueue<ChannelSyncJob>(job =>
                job.ExecuteAsync(channelId, CancellationToken.None));

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Queued sync jobs for channel: {ChannelId}", channelId);
            }
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error queuing channel sync job");
            }

            throw;
        }
    }
}