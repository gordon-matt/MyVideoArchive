using Hangfire;
using MyVideoArchive.Infrastructure;

namespace MyVideoArchive.Services.Jobs;

/// <summary>
/// Hangfire job that downloads sidecar subtitle (.vtt) files for already-downloaded videos
/// that don't have any. Intended for one-off backfills after the user enables the Subtitles
/// feature, but is also scheduled weekly so long-running archives stay up to date when
/// platforms publish new caption tracks.
///
/// Runs in the dedicated "downloads" queue so it serializes with <see cref="VideoDownloadJob"/>
/// and shares the same throttling characteristics (single worker, one request at a time).
/// </summary>
[Queue("downloads")]
public class SubtitleBackfillJob
{
    private readonly ILogger<SubtitleBackfillJob> logger;
    private readonly IConfiguration configuration;
    private readonly VideoDownloaderFactory downloaderFactory;
    private readonly IRepository<Video> videoRepository;

    public SubtitleBackfillJob(
        ILogger<SubtitleBackfillJob> logger,
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
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        // Honour the global subtitle toggle and the backfill-specific toggle.
        // Both are checked at execution time so flipping either flag in appsettings
        // takes effect on the next run without needing to redeploy.
        if (!configuration.GetValue<bool>("Subtitles:Enabled", false))
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Subtitle backfill skipped — Subtitles:Enabled is false");
            }
            return;
        }

        if (!configuration.GetValue<bool>("Subtitles:BackfillEnabled", true))
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Subtitle backfill skipped — Subtitles:BackfillEnabled is false");
            }
            return;
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Starting subtitle backfill job");
        }

        // Only videos we've actually downloaded are candidates — we need the real video file
        // on disk so the new .vtt sidecars land in the correct folder.
        var candidates = await videoRepository.FindAsync(new SearchOptions<Video>
        {
            CancellationToken = cancellationToken,
            Query = x =>
                x.DownloadedAt != null
                && x.FilePath != null
                && !x.IsIgnored
                && x.Platform != "Custom"
                && !x.NeedsMetadataReview,
            Include = query => query.Include(x => x.Channel)
        });

        if (candidates.Count == 0)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Subtitle backfill: no downloaded videos to examine");
            }
            return;
        }

        int minDelay = configuration.GetValue<int>("VideoDownload:Throttle:MinDelaySeconds", 10);
        int maxDelay = configuration.GetValue<int>("VideoDownload:Throttle:MaxDelaySeconds", 60);

        int examined = 0;
        int skippedExisting = 0;
        int attempted = 0;
        int succeeded = 0;
        int failed = 0;

        foreach (var video in candidates)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            examined++;

            try
            {
                if (string.IsNullOrEmpty(video.FilePath) || !File.Exists(video.FilePath))
                {
                    continue;
                }

                if (HasExistingSubtitles(video.FilePath))
                {
                    skippedExisting++;
                    continue;
                }

                var downloader = downloaderFactory.GetDownloaderByPlatform(video.Platform);
                if (downloader is null)
                {
                    continue;
                }

                string? outputDir = Path.GetDirectoryName(video.FilePath);
                if (string.IsNullOrEmpty(outputDir))
                {
                    continue;
                }

                // Jitter between network requests, same shape as VideoDownloadJob, so we don't
                // trip rate limits on large backfills.
                if (attempted > 0 && maxDelay > minDelay)
                {
                    var jitter = TimeSpan.FromSeconds(Random.Shared.Next(minDelay, maxDelay + 1));
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogDebug("Subtitle backfill: waiting {Delay}s before next request", jitter.TotalSeconds);
                    }
                    await Task.Delay(jitter, cancellationToken);
                }

                attempted++;

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation(
                        "Subtitle backfill: fetching subs for video {VideoId} ('{Title}')",
                        video.VideoId, video.Title);
                }

                bool ok = await downloader.DownloadSubtitlesAsync(video.Url, outputDir, cancellationToken);
                if (ok && HasExistingSubtitles(video.FilePath))
                {
                    succeeded++;
                }
                else
                {
                    failed++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(ex, "Subtitle backfill: error processing video {VideoId}", video.VideoId);
                }
            }
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Subtitle backfill complete. Examined: {Examined}, already had subs: {Existing}, " +
                "attempted: {Attempted}, succeeded: {Succeeded}, failed: {Failed}",
                examined, skippedExisting, attempted, succeeded, failed);
        }
    }

    /// <summary>
    /// True when at least one sidecar ".vtt" file sits next to the video file. yt-dlp names
    /// them "&lt;id&gt;.&lt;lang&gt;.vtt" so we match on the video's filename-without-extension.
    /// </summary>
    private static bool HasExistingSubtitles(string videoFilePath)
    {
        string? directory = Path.GetDirectoryName(videoFilePath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        string videoBaseName = Path.GetFileNameWithoutExtension(videoFilePath);
        if (string.IsNullOrEmpty(videoBaseName))
        {
            return false;
        }

        return Directory.EnumerateFiles(directory, $"{videoBaseName}.*.vtt", SearchOption.TopDirectoryOnly).Any();
    }
}
