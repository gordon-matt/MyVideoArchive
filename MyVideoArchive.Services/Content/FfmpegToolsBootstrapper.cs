using System.Runtime.InteropServices;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace MyVideoArchive.Services.Content;

/// <summary>
/// Ensures ffmpeg and ffprobe are available where Xabe.FFmpeg expects them, then calls
/// <see cref="FFmpeg.SetExecutablesPath(string)"/>.
/// </summary>
/// <remarks>
/// Xabe validates that <b>both</b> executables exist in the configured directory (see
/// <c>FFmpeg.ValidateExecutables</c> in the Xabe source). YoutubeDLSharp's
/// <c>Utils.DownloadFFmpeg()</c> only supplies an ffmpeg binary for yt-dlp — it does not
/// guarantee ffprobe beside it — so thumbnail generation via Xabe can still fail until
/// ffprobe is present. This bootstrapper downloads the official ffbinaries bundle (ffmpeg + ffprobe)
/// via <see cref="FFmpegDownloader.GetLatestVersion"/> when either file is missing.
/// </remarks>
public static class FfmpegToolsBootstrapper
{
    public static async Task ConfigureXabeAsync(
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        string ffmpegExeName = isWindows ? "ffmpeg.exe" : "ffmpeg";
        string ffprobeExeName = isWindows ? "ffprobe.exe" : "ffprobe";

        string? toolsDir = ResolveToolsDirectory(configuration, ffmpegExeName);
        if (toolsDir is null)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "No local FFmpeg directory found (YoutubeDL:FFmpegPath unset and no ffmpeg next to the app). " +
                    "Xabe will look for ffmpeg and ffprobe on PATH.");
            }

            return;
        }

        toolsDir = Path.GetFullPath(toolsDir);
        string ffmpegFull = Path.Combine(toolsDir, ffmpegExeName);
        string ffprobeFull = Path.Combine(toolsDir, ffprobeExeName);

        bool haveFfmpeg = File.Exists(ffmpegFull);
        bool haveFfprobe = File.Exists(ffprobeFull);

        if (!haveFfmpeg || !haveFfprobe)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "FFmpeg tools in {Dir}: ffmpeg={HaveFfmpeg}, ffprobe={HaveFfprobe}. " +
                    "Downloading official ffmpeg+ffprobe bundle (Xabe.FFmpeg.Downloader)…",
                    toolsDir,
                    haveFfmpeg,
                    haveFfprobe);
            }

            await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, toolsDir, progress: null)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (!File.Exists(ffmpegFull) || !File.Exists(ffprobeFull))
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    "After download, ffmpeg and ffprobe were not both found under {Dir}. " +
                    "Thumbnail generation via Xabe may still fail.",
                    toolsDir);
            }

            return;
        }

        FFmpeg.SetExecutablesPath(toolsDir);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Xabe.FFmpeg executables path set to {Dir}", toolsDir);
        }
    }

    /// <summary>
    /// Directory that should contain ffmpeg (and, after bootstrap, ffprobe), or null to use PATH only.
    /// </summary>
    private static string? ResolveToolsDirectory(IConfiguration configuration, string ffmpegExeName)
    {
        string? configured = configuration["YoutubeDL:FFmpegPath"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (File.Exists(configured))
            {
                return Path.GetDirectoryName(configured);
            }

            if (Directory.Exists(configured))
            {
                return configured;
            }
        }

        foreach (string dir in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            if (File.Exists(Path.Combine(dir, ffmpegExeName)))
            {
                return dir;
            }
        }

        return null;
    }
}
