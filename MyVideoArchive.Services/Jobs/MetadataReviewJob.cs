using MyVideoArchive.Infrastructure;

namespace MyVideoArchive.Services.Jobs;

/// <summary>
/// Hangfire job that runs weekly to retry fetching metadata for videos that were previously
/// unavailable (e.g. private or deleted videos where <see cref="Video.NeedsMetadataReview"/> is true).
/// When valid metadata is found the video is updated and the review flag is cleared.
/// </summary>
public class MetadataReviewJob
{
    private readonly ILogger<MetadataReviewJob> logger;
    private readonly IConfiguration configuration;
    private readonly VideoMetadataProviderFactory metadataProviderFactory;
    private readonly ThumbnailService thumbnailService;
    private readonly IRepository<Video> videoRepository;

    public MetadataReviewJob(
        ILogger<MetadataReviewJob> logger,
        IConfiguration configuration,
        VideoMetadataProviderFactory metadataProviderFactory,
        ThumbnailService thumbnailService,
        IRepository<Video> videoRepository)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.metadataProviderFactory = metadataProviderFactory;
        this.thumbnailService = thumbnailService;
        this.videoRepository = videoRepository;
    }

    [HangfireSkipWhenPreviousInstanceIsRunningFilter]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Starting metadata review job");
        }

        string downloadPath = configuration.GetValue<string>("VideoDownload:OutputPath")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

        var videos = await videoRepository.FindAsync(new SearchOptions<Video>
        {
            CancellationToken = cancellationToken,
            Query = x => x.NeedsMetadataReview && x.Platform != "Custom",
            Include = query => query.Include(x => x.Channel)
        });

        if (videos.Count == 0)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("No videos flagged for metadata review");
            }

            return;
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Found {Count} video(s) flagged for metadata review", videos.Count);
        }

        int resolved = 0;

        foreach (var video in videos)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var provider = metadataProviderFactory.GetProviderByPlatform(video.Platform);
                if (provider is null)
                {
                    if (logger.IsEnabled(LogLevel.Warning))
                    {
                        logger.LogWarning("No metadata provider for platform {Platform}, skipping video {VideoId}", video.Platform, video.VideoId);
                    }

                    continue;
                }

                var metadata = await provider.GetVideoMetadataAsync(video.Url, cancellationToken);

                if (metadata is null || metadata.Title == Constants.PrivateVideoTitle)
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogDebug("Video {VideoId} still unavailable; keeping NeedsMetadataReview flag", video.VideoId);
                    }

                    continue;
                }

                if (metadata.Title == Constants.DeletedVideoTitle)
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogDebug("Video {VideoId} is deleted; setting NeedsMetadataReview to false", video.VideoId);
                    }

                    // TODO: We need a way to mark videos as deleted, so we can display in UI
                    video.NeedsMetadataReview = false;
                    await videoRepository.UpdateAsync(video, ContextOptions.ForCancellationToken(cancellationToken));

                    continue;
                }

                // Metadata is now available — update the video
                video.Title = metadata.Title;
                video.Description = metadata.Description;
                video.Duration = metadata.Duration;
                video.ViewCount = metadata.ViewCount;
                video.LikeCount = metadata.LikeCount;
                video.NeedsMetadataReview = false;

                string thumbnailDir = Path.Combine(downloadPath, video.Channel.ChannelId);
                video.ThumbnailUrl = await thumbnailService.DownloadAndSaveAsync(
                    metadata.ThumbnailUrl, thumbnailDir, video.VideoId, cancellationToken)
                    ?? metadata.ThumbnailUrl;

                await videoRepository.UpdateAsync(video, ContextOptions.ForCancellationToken(cancellationToken));
                resolved++;

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Resolved metadata for video {VideoId}: '{Title}'", video.VideoId, video.Title);
                }
            }
            catch (Exception ex)
            {
                if (logger.IsEnabled(LogLevel.Error))
                {
                    logger.LogError(ex, "Error reviewing metadata for video {VideoId}", video.VideoId);
                }
            }
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Metadata review job complete. Resolved: {Resolved}/{Total}",
                resolved, videos.Count);
        }
    }
}