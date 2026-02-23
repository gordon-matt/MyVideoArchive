using System.Runtime.InteropServices;
using YoutubeDLSharp;

namespace MyVideoArchive.Services;

/// <summary>
/// Service for initializing and configuring YoutubeDL instances
/// </summary>
public class YoutubeDLInitializer
{
    private readonly ILogger<YoutubeDLInitializer> logger;
    private readonly IConfiguration configuration;
    private static YoutubeDL? ytdl;
    private static readonly SemaphoreSlim initLock = new(1, 1);

    public YoutubeDLInitializer(
        ILogger<YoutubeDLInitializer> logger,
        IConfiguration configuration)
    {
        this.logger = logger;
        this.configuration = configuration;
    }

    public async Task<YoutubeDL> GetInstanceAsync()
    {
        if (ytdl != null)
        {
            return ytdl;
        }

        await initLock.WaitAsync();

        try
        {
            if (ytdl != null)
            {
                return ytdl;
            }

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Initializing YoutubeDL...");
            }

            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            string ytDlpName = isWindows ? "yt-dlp.exe" : "yt-dlp";
            string ffmpegName = isWindows ? "ffmpeg.exe" : "ffmpeg";

            // Read configured paths; treat empty/null the same
            string? configuredYtDlpPath = configuration["YoutubeDL:ExecutablePath"];
            if (string.IsNullOrWhiteSpace(configuredYtDlpPath))
            {
                configuredYtDlpPath = null;
            }

            string? configuredFfmpegPath = configuration["YoutubeDL:FFmpegPath"];
            if (string.IsNullOrWhiteSpace(configuredFfmpegPath))
            {
                configuredFfmpegPath = null;
            }

            string ytDlpPath = configuredYtDlpPath
                ?? Path.Combine(Directory.GetCurrentDirectory(), ytDlpName);

            string ffmpegPath = configuredFfmpegPath
                ?? Path.Combine(Directory.GetCurrentDirectory(), ffmpegName);

            string downloadPath = configuration["VideoDownload:OutputPath"]
                ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("yt-dlp path : {Path} (exists: {Exists})", ytDlpPath, File.Exists(ytDlpPath));
                logger.LogInformation("ffmpeg path : {Path} (exists: {Exists})", ffmpegPath, File.Exists(ffmpegPath));
                logger.LogInformation("PATH        : {Path}", Environment.GetEnvironmentVariable("PATH"));
            }

            if (!File.Exists(ytDlpPath))
            {
                if (configuredYtDlpPath != null)
                {
                    // Explicit path configured (e.g. Docker) but file missing — the image was not built correctly.
                    // Never fall back to Utils.DownloadYtDlp(): that downloads a Python script
                    // (#!/usr/bin/env python3) which causes "env: can't execute 'python3'" errors.
                    throw new InvalidOperationException(
                        $"yt-dlp not found at configured path '{ytDlpPath}'. " +
                        "Rebuild the image: docker compose build --no-cache app && docker compose up -d");
                }

                // No configured path (local/Windows dev) — download as fallback
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("yt-dlp not found at '{Path}'; downloading.", ytDlpPath);
                }

                await Utils.DownloadYtDlp();
            }

            if (!File.Exists(ytDlpPath))
            {
                throw new InvalidOperationException($"yt-dlp not found at '{ytDlpPath}'.");
            }

            if (!File.Exists(ffmpegPath))
            {
                if (configuredFfmpegPath != null)
                {
                    throw new InvalidOperationException(
                        $"ffmpeg not found at configured path '{ffmpegPath}'. " +
                        "Rebuild the image: docker compose build --no-cache app && docker compose up -d");
                }

                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("ffmpeg not found at '{Path}'; downloading.", ffmpegPath);
                }

                await Utils.DownloadFFmpeg();
            }

            if (!Directory.Exists(downloadPath))
            {
                Directory.CreateDirectory(downloadPath);
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Created downloads directory: {Path}", downloadPath);
                }
            }

            ytdl = new YoutubeDL
            {
                YoutubeDLPath = ytDlpPath,
                FFmpegPath = ffmpegPath,
                OutputFolder = downloadPath
            };

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("YoutubeDL initialized. yt-dlp: {YtDlp}, ffmpeg: {Ffmpeg}", ytDlpPath, ffmpegPath);
            }

            return ytdl;
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Failed to initialize YoutubeDL");
            }

            throw;
        }
        finally
        {
            initLock.Release();
        }
    }
}