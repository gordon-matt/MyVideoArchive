using MyVideoArchive.Infrastructure;

namespace MyVideoArchive.Services.Jobs;

/// <summary>
/// Hangfire job for downloading videos
/// </summary>
public class VideoDownloadJob
{
    private readonly ILogger<VideoDownloadJob> logger;
    private readonly IConfiguration configuration;
    private readonly VideoDownloaderFactory downloaderFactory;
    private readonly IRepository<Video> videoRepository;

    public VideoDownloadJob(
        ILogger<VideoDownloadJob> logger,
        IConfiguration configuration,
        VideoDownloaderFactory downloaderFactory,
        IRepository<Video> videoRepository)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.downloaderFactory = downloaderFactory;
        this.videoRepository = videoRepository;
    }

    [HangfireSkipWhenPreviousInstanceIsRunningFilter]
    public async Task ExecuteAsync(int videoId, CancellationToken cancellationToken = default)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Starting video download job for video ID: {VideoId}", videoId);
        }

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
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Video with ID {VideoId} not found", videoId);
                }

                return;
            }

            // Skip if already downloaded
            if (!string.IsNullOrEmpty(video.FilePath) && File.Exists(video.FilePath))
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Video {VideoId} already downloaded", videoId);
                }

                return;
            }

            // Get the appropriate downloader
            var downloader = downloaderFactory.GetDownloaderByPlatform(video.Platform);
            if (downloader is null)
            {
                if (logger.IsEnabled(LogLevel.Error))
                {
                    logger.LogError("No downloader found for platform: {Platform}", video.Platform);
                }

                return;
            }

            // Get download path from configuration
            string downloadPath = configuration.GetValue<string>("VideoDownload:OutputPath")
                ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

            // Create channel-specific subdirectory
            string channelPath = Path.Combine(downloadPath, SanitizeFileName(video.Channel.Name));

            // Download the video
            var progress = new Progress<double>(p =>
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Download progress for video {VideoId}: {Progress:P0}", videoId, p);
                }
            });

            string filePath = await downloader.DownloadVideoAsync(
                video.Url,
                channelPath,
                progress,
                cancellationToken);

            // Update video record
            video.FilePath = filePath;
            video.FileSize = new FileInfo(filePath).Length;
            video.DownloadedAt = DateTime.UtcNow;
            video.IsQueued = false;

            await videoRepository.UpdateAsync(video, ContextOptions.ForCancellationToken(cancellationToken));
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Successfully downloaded video {VideoId} to {FilePath}", videoId, filePath);
            }
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error downloading video {VideoId}", videoId);
            }

            // Reset IsQueued flag on failure so it can be retried
            try
            {
                var video = await videoRepository.FindOneAsync(videoId);
                if (video is not null && video.IsQueued)
                {
                    video.IsQueued = false;
                    await videoRepository.UpdateAsync(video, ContextOptions.ForCancellationToken(cancellationToken));
                }
            }
            catch (Exception resetEx)
            {
                if (logger.IsEnabled(LogLevel.Error))
                {
                    logger.LogError(resetEx, "Error resetting IsQueued flag for video {VideoId}", videoId);
                }
            }

            throw;
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
    }
}