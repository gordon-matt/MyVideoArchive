using Heroicsoft.NetForge.Video;

namespace MyVideoArchive.Services.Content;

/// <summary>
/// Ensures ffmpeg and ffprobe are available where the shared video helpers expect them.
/// YoutubeDLSharp's downloader only supplies ffmpeg for yt-dlp, so thumbnail capture still
/// needs ffprobe beside it. When a tools directory is already known, a missing binary is
/// filled in with the official build. When no directory is known, Xabe is left to use PATH.
/// </summary>
public static class FfmpegToolsBootstrapper
{
    public static Task ConfigureXabeAsync(
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        string? toolsDir = ResolveToolsDirectory(configuration);

        return FfmpegBootstrap.EnsureAsync(
            new FfmpegBootstrapOptions
            {
                ConfiguredPath = toolsDir,
                DownloadDirectory = toolsDir,
                SearchWellKnownLocations = false,
                SearchApplicationDirectories = false,
                DownloadIfMissing = toolsDir is not null,
                RequireFfprobe = true,
            },
            logger,
            cancellationToken);
    }

    private static string? ResolveToolsDirectory(IConfiguration configuration)
    {
        bool windows = OperatingSystem.IsWindows();
        string ffmpegExeName = windows ? "ffmpeg.exe" : "ffmpeg";
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
