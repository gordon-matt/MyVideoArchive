using Heroicsoft.NetForge.Video;
using Microsoft.Extensions.Logging.Abstractions;

namespace MyVideoArchive.Services.Content;

/// <summary>
/// Optional post-download safety net that shrinks videos taller than a configured maximum
/// height using ffmpeg.
/// </summary>
/// <remarks>
/// Some platforms (notably Odysee/LBRY) expose only a single "original" quality through
/// yt-dlp — there is no lower-resolution format to select, so <c>VideoDownload:VideoQuality</c>
/// has nothing to act on. This is the only way to actually reduce those files' size. Controlled
/// by the <c>VideoDownload:PostProcessDownscale</c> config section (disabled by default). Videos
/// already at or below the configured height are left completely untouched (only probed).
/// </remarks>
public static class VideoDownscaleService
{
    public static Task<string> EnsureMaxHeightAsync(
        string? ffmpegPath,
        string videoFilePath,
        IConfiguration configuration,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        bool enabled = configuration.GetValue("VideoDownload:PostProcessDownscale:Enabled", false);
        if (!enabled || string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath) || !File.Exists(videoFilePath))
        {
            return Task.FromResult(videoFilePath);
        }

        int maxHeight = configuration.GetValue("VideoDownload:PostProcessDownscale:MaxHeight", 720);
        int crf = configuration.GetValue("VideoDownload:PostProcessDownscale:Crf", 23);
        string preset = configuration.GetValue<string>("VideoDownload:PostProcessDownscale:Preset") ?? "medium";

        ILogger<VideoTranscoder> typed = logger is null
            ? NullLogger<VideoTranscoder>.Instance
            : new LoggerAdapter<VideoTranscoder>(logger);

        return new VideoTranscoder(typed).DownscaleToMaxHeightAsync(
            new VideoDownscaleRequest
            {
                FfmpegPath = ffmpegPath,
                VideoPath = videoFilePath,
                MaxHeight = maxHeight,
                Crf = crf,
                Preset = preset,
            },
            cancellationToken);
    }
}
