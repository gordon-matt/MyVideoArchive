using Hangfire;
using Hangfire.Common;
using MyVideoArchive.Infrastructure;

namespace MyVideoArchive.Services.Jobs;

/// <summary>
/// Hangfire job for downloading videos.
/// Runs in the dedicated "downloads" queue (single worker) to serialize downloads and avoid
/// hammering YouTube. Each run adds a configurable random delay, and a batch window check
/// re-schedules the job when too many downloads have occurred in the last N minutes.
/// </summary>
[Queue("downloads")]
public class VideoDownloadJob
{
    private readonly ILogger<VideoDownloadJob> logger;
    private readonly IConfiguration configuration;
    private readonly IBackgroundJobClient backgroundJobClient;
    private readonly VideoDownloaderFactory downloaderFactory;
    private readonly IRepository<Video> videoRepository;

    public VideoDownloadJob(
        ILogger<VideoDownloadJob> logger,
        IConfiguration configuration,
        IBackgroundJobClient backgroundJobClient,
        VideoDownloaderFactory downloaderFactory,
        IRepository<Video> videoRepository)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.backgroundJobClient = backgroundJobClient;
        this.downloaderFactory = downloaderFactory;
        this.videoRepository = videoRepository;
    }

    [HangfireSkipWhenPreviousInstanceIsRunningFilter]
    [AutomaticRetry(Attempts = 0)]
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
                logger.LogWarning("Video with ID {VideoId} not found", videoId);
                return;
            }

            if (video.DownloadFailed)
            {
                logger.LogInformation(
                    "Skipping video {VideoId} — previously marked as failed. " +
                    "Manually import the file and run a file system scan to recover it.",
                    videoId);
                video.IsQueued = false;
                await videoRepository.UpdateAsync(video, ContextOptions.ForCancellationToken(cancellationToken));
                return;
            }

            // Skip if already downloaded
            if (!string.IsNullOrEmpty(video.FilePath) && File.Exists(video.FilePath))
            {
                logger.LogInformation("Video {VideoId} already downloaded", videoId);
                video.IsQueued = false;
                await videoRepository.UpdateAsync(video, ContextOptions.ForCancellationToken(cancellationToken));
                return;
            }

            // ── Throttle: batch window check ─────────────────────────────────
            int batchSize = configuration.GetValue<int>("VideoDownload:Throttle:BatchSize", 25);
            int batchWindowMinutes = configuration.GetValue<int>("VideoDownload:Throttle:BatchWindowMinutes", 30);

            var windowStart = DateTime.UtcNow.AddMinutes(-batchWindowMinutes);
            var recentDownloadCount = await videoRepository.CountAsync(
                x => x.DownloadedAt != null && x.DownloadedAt >= windowStart,
                ContextOptions.ForCancellationToken(cancellationToken));

            if (recentDownloadCount >= batchSize)
            {
                // Find the earliest download in the current window so we know when the window slides open
                var oldestInWindow = await videoRepository.FindOneAsync(new SearchOptions<Video>
                {
                    CancellationToken = cancellationToken,
                    Query = x => x.DownloadedAt != null && x.DownloadedAt >= windowStart,
                    OrderBy = q => q.OrderBy(x => x.DownloadedAt)
                });

                var rescheduleDelay = oldestInWindow?.DownloadedAt.HasValue == true
                    ? (oldestInWindow.DownloadedAt!.Value.AddMinutes(batchWindowMinutes) - DateTime.UtcNow)
                        .Add(TimeSpan.FromSeconds(5))
                    : TimeSpan.FromMinutes(batchWindowMinutes);

                // Clamp to at least 1 minute in case of clock skew
                if (rescheduleDelay < TimeSpan.FromMinutes(1))
                    rescheduleDelay = TimeSpan.FromMinutes(1);

                logger.LogInformation(
                    "Batch limit of {BatchSize} downloads per {Window} min reached. " +
                    "Rescheduling video {VideoId} to run in {Delay}.",
                    batchSize, batchWindowMinutes, videoId, rescheduleDelay);

                backgroundJobClient.Schedule<VideoDownloadJob>(
                    job => job.ExecuteAsync(videoId, CancellationToken.None),
                    rescheduleDelay);

                return; // Exit cleanly — the rescheduled job will handle the download
            }

            // ── Throttle: random inter-download delay ─────────────────────────
            int minDelay = configuration.GetValue<int>("VideoDownload:Throttle:MinDelaySeconds", 10);
            int maxDelay = configuration.GetValue<int>("VideoDownload:Throttle:MaxDelaySeconds", 60);

            if (maxDelay > minDelay)
            {
                var jitter = TimeSpan.FromSeconds(Random.Shared.Next(minDelay, maxDelay + 1));
                logger.LogInformation(
                    "Waiting {Delay}s before downloading video {VideoId}", jitter.TotalSeconds, videoId);
                await Task.Delay(jitter, cancellationToken);
            }

            // ── Download ──────────────────────────────────────────────────────
            var downloader = downloaderFactory.GetDownloaderByPlatform(video.Platform);
            if (downloader is null)
            {
                logger.LogError("No downloader found for platform: {Platform}", video.Platform);
                return;
            }

            string downloadPath = configuration.GetValue<string>("VideoDownload:OutputPath")
                ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

            string channelPath = Path.Combine(downloadPath, video.Channel.ChannelId);

            var progress = new Progress<double>(p =>
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("Download progress for video {VideoId}: {Progress:P0}", videoId, p);
                }
            });

            string filePath = await downloader.DownloadVideoAsync(
                video.Url,
                channelPath,
                progress,
                cancellationToken);

            video.FilePath = filePath;
            video.FileSize = new FileInfo(filePath).Length;
            video.DownloadedAt = DateTime.UtcNow;
            video.IsQueued = false;
            video.DownloadFailed = false;

            await videoRepository.UpdateAsync(video, ContextOptions.ForCancellationToken(cancellationToken));

            logger.LogInformation("Successfully downloaded video {VideoId} to {FilePath}", videoId, filePath);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Download cancelled for video {VideoId}", videoId);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to download video {VideoId}. Marking as failed.", videoId);

            try
            {
                var video = await videoRepository.FindOneAsync(videoId);
                if (video is not null)
                {
                    video.IsQueued = false;
                    video.DownloadFailed = true;
                    await videoRepository.UpdateAsync(video, ContextOptions.ForCancellationToken(cancellationToken));
                }
            }
            catch (Exception resetEx)
            {
                logger.LogError(resetEx, "Error marking video {VideoId} as failed", videoId);
            }

            // Do NOT rethrow — Hangfire should not retry permanently-failed downloads.
            // The video is flagged as DownloadFailed for user action.
        }
    }
}
