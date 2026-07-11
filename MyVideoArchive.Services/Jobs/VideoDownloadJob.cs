using Hangfire;
using MyVideoArchive.Infrastructure;
using MyVideoArchive.Services.Content;

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
    private readonly ThumbnailService thumbnailService;

    public VideoDownloadJob(
        ILogger<VideoDownloadJob> logger,
        IConfiguration configuration,
        IBackgroundJobClient backgroundJobClient,
        VideoDownloaderFactory downloaderFactory,
        IRepository<Video> videoRepository,
        ThumbnailService thumbnailService)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.backgroundJobClient = backgroundJobClient;
        this.downloaderFactory = downloaderFactory;
        this.videoRepository = videoRepository;
        this.thumbnailService = thumbnailService;
    }

    [HangfireSkipWhenPreviousInstanceIsRunningFilter]
    [AutomaticRetry(Attempts = 0)]
    public Task ExecuteAsync(int videoId, CancellationToken cancellationToken = default)
        => RunDownloadAsync(videoId, 0, cancellationToken);

    /// <summary>
    /// Retry entry point used when a download was rescheduled after a transient failure
    /// (e.g. HTTP 429 rate limiting). <paramref name="rateLimitAttempt"/> tracks how many
    /// rate-limit retries have already occurred so the backoff can escalate and eventually give up.
    /// </summary>
    [HangfireSkipWhenPreviousInstanceIsRunningFilter]
    [AutomaticRetry(Attempts = 0)]
    public Task RetryDownloadAsync(int videoId, int rateLimitAttempt, CancellationToken cancellationToken = default)
        => RunDownloadAsync(videoId, rateLimitAttempt, cancellationToken);

    private async Task RunDownloadAsync(int videoId, int rateLimitAttempt, CancellationToken cancellationToken)
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

            if (video.DownloadFailed)
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation(
                        "Skipping video {VideoId} — previously marked as failed. " +
                        "Manually import the file and run a file system scan to recover it.",
                        videoId);
                }

                video.IsQueued = false;
                await videoRepository.UpdateAsync(video, ContextOptions.ForCancellationToken(cancellationToken));
                return;
            }

            // Skip if already downloaded
            if (!string.IsNullOrEmpty(video.FilePath) && File.Exists(video.FilePath))
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Video {VideoId} already downloaded", videoId);
                }

                video.IsQueued = false;
                await videoRepository.UpdateAsync(video, ContextOptions.ForCancellationToken(cancellationToken));
                return;
            }

            // ── Throttle: batch window check ─────────────────────────────────
            int batchSize = configuration.GetValue<int>("VideoDownload:Throttle:BatchSize", 25);
            int batchWindowMinutes = configuration.GetValue<int>("VideoDownload:Throttle:BatchWindowMinutes", 30);

            var windowStart = DateTime.UtcNow.AddMinutes(-batchWindowMinutes);
            int recentDownloadCount = await videoRepository.CountAsync(
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
                    ? (oldestInWindow.DownloadedAt!.Value.AddMinutes(batchWindowMinutes) - DateTime.UtcNow).Add(TimeSpan.FromSeconds(5))
                    : TimeSpan.FromMinutes(batchWindowMinutes);

                // Clamp to at least 1 minute in case of clock skew
                if (rescheduleDelay < TimeSpan.FromMinutes(1))
                {
                    rescheduleDelay = TimeSpan.FromMinutes(1);
                }

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation(
                        "Batch limit of {BatchSize} downloads per {Window} min reached. " +
                        "Rescheduling video {VideoId} to run in {Delay}.",
                        batchSize, batchWindowMinutes, videoId, rescheduleDelay);
                }

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
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Waiting {Delay}s before downloading video {VideoId}", jitter.TotalSeconds, videoId);
                }

                await Task.Delay(jitter, cancellationToken);
            }

            // ── Download ──────────────────────────────────────────────────────
            var downloader = downloaderFactory.GetDownloaderByPlatform(video.Platform);
            if (downloader is null)
            {
                if (logger.IsEnabled(LogLevel.Error))
                {
                    logger.LogError("No downloader found for platform: {Platform}", video.Platform);
                }

                return;
            }

            string downloadPath = configuration.GetValue<string>("VideoDownload:OutputPath")
                ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

            string channelPath = CustomChannelPathHelper.GetChannelDirectory(
                downloadPath, video.Channel.Platform, video.Channel.ChannelId);

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

            // Save the thumbnail to a local file now that the video has been downloaded.
            // If ThumbnailUrl is still a remote HTTP URL, download and save it.
            // If that fails (or there is no URL), generate a frame from the video file.
            if (!ThumbnailService.IsLocalUrl(video.ThumbnailUrl))
            {
                string? localUrl = null;

                if (ThumbnailService.IsRemoteUrl(video.ThumbnailUrl))
                {
                    localUrl = await thumbnailService.DownloadAndSaveAsync(
                        video.ThumbnailUrl, channelPath, video.VideoId, downloadPath, cancellationToken);
                }

                if (localUrl == null)
                {
                    localUrl = await thumbnailService.GenerateFromVideoAsync(
                        filePath, channelPath, video.VideoId, downloadPath, cancellationToken);
                }

                if (!string.IsNullOrEmpty(localUrl))
                {
                    video.ThumbnailUrl = localUrl;
                }
            }

            await videoRepository.UpdateAsync(video, ContextOptions.ForCancellationToken(cancellationToken));
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Successfully downloaded video {VideoId} to {FilePath}", videoId, filePath);
            }
        }
        catch (OperationCanceledException)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("Download cancelled for video {VideoId}", videoId);
            }

            throw;
        }
        catch (TransientDownloadException ex)
        {
            await HandleTransientFailureAsync(videoId, rateLimitAttempt, ex, cancellationToken);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Failed to download video {VideoId}. Marking as failed.", videoId);
            }

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
                if (logger.IsEnabled(LogLevel.Error))
                {
                    logger.LogError(resetEx, "Error marking video {VideoId} as failed", videoId);
                }
            }

            // Do NOT rethrow — Hangfire should not retry permanently-failed downloads.
            // The video is flagged as DownloadFailed for user action.
        }
    }

    /// <summary>
    /// Handles a transient (retryable) download failure such as HTTP 429 rate limiting.
    /// Reschedules the video with exponential backoff instead of marking it permanently failed,
    /// giving up only after <c>VideoDownload:Throttle:MaxRateLimitRetries</c> attempts.
    /// </summary>
    private async Task HandleTransientFailureAsync(
        int videoId,
        int rateLimitAttempt,
        TransientDownloadException ex,
        CancellationToken cancellationToken)
    {
        int maxRetries = configuration.GetValue<int>("VideoDownload:Throttle:MaxRateLimitRetries", 6);
        int nextAttempt = rateLimitAttempt + 1;

        if (nextAttempt > maxRetries)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex,
                    "Video {VideoId} still rate limited after {Attempts} attempts. Marking as failed.",
                    videoId, rateLimitAttempt);
            }

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
                if (logger.IsEnabled(LogLevel.Error))
                {
                    logger.LogError(resetEx, "Error marking video {VideoId} as failed", videoId);
                }
            }

            return;
        }

        // Exponential backoff capped at 60 minutes: 2, 4, 8, 16, 32, 60 minutes.
        int backoffMinutes = Math.Min(60, (int)Math.Pow(2, nextAttempt));
        var delay = TimeSpan.FromMinutes(backoffMinutes);

        if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning(
                "Video {VideoId} was rate limited (attempt {Attempt}/{Max}). Rescheduling in {Delay} min. {Reason}",
                videoId, nextAttempt, maxRetries, backoffMinutes, ex.Message);
        }

        backgroundJobClient.Schedule<VideoDownloadJob>(
            job => job.RetryDownloadAsync(videoId, nextAttempt, CancellationToken.None),
            delay);
    }
}