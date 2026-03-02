using System.Text.RegularExpressions;
using MyVideoArchive.Services.Abstractions;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace MyVideoArchive.Services.Providers;

/// <summary>
/// YouTube implementation of video downloader using yt-dlp
/// </summary>
public partial class YouTubeDownloader : IVideoDownloader
{
    private readonly ILogger<YouTubeDownloader> logger;
    private readonly IConfiguration configuration;
    private readonly YoutubeDL ytdl;

    public string PlatformName => "YouTube";

    [GeneratedRegex(@"(youtube\.com|youtu\.be)", RegexOptions.IgnoreCase)]
    private static partial Regex YouTubeUrlRegex();

    public YouTubeDownloader(
        ILogger<YouTubeDownloader> logger,
        IConfiguration configuration,
        YoutubeDL ytdl)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.ytdl = ytdl;
    }

    public bool CanHandle(string url) => YouTubeUrlRegex().IsMatch(url);

    public async Task<string> DownloadVideoAsync(
        string videoUrl,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Downloading video from: {Url}", videoUrl);
            }

            // Ensure output directory exists
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            // Get video quality from configuration
            string videoQuality = configuration.GetValue<string>("VideoDownload:VideoQuality")
                ?? Constants.BestDownloadQuality;

            // Configure download options
            var options = new OptionSet
            {
                Format = videoQuality,
                MergeOutputFormat = DownloadMergeFormat.Mp4,
                Output = Path.Combine(outputPath, "%(id)s.%(ext)s"),
                WriteInfoJson = true,
                WriteThumbnail = true,
                EmbedThumbnail = true,
                EmbedMetadata = true,
                NoPlaylist = true // Download only the single video, not playlist
            };

            // Note: Progress reporting via OutputReceived is not available in this version of YoutubeDLSharp
            // Progress will be logged but not reported via IProgress

            RunResult<string?> result = null;

            try
            {
                result = await ytdl.RunVideoDownload(videoUrl, overrideOptions: options, ct: cancellationToken);
            }
            catch
            {
                if (options.Format != Constants.BestDownloadQuality)
                {
                    if (logger.IsEnabled(LogLevel.Warning))
                    {
                        logger.LogWarning("Failed to download video from {Url}. Attempting fallback to best quality", videoUrl);
                    }

                    // Fallback in case of failure with specified quality
                    options.Format = Constants.BestDownloadQuality;
                    result = await ytdl.RunVideoDownload(videoUrl, overrideOptions: options, ct: cancellationToken);
                }
            }

            if (!result!.Success)
            {
                string errorMessage = string.Join(", ", result.ErrorOutput);
                if (logger.IsEnabled(LogLevel.Error))
                {
                    if (logger.IsEnabled(LogLevel.Error))
                    {
                        logger.LogError("Failed to download video from {Url}: {Error}", videoUrl, errorMessage);
                    }

                }

                throw new InvalidOperationException($"Failed to download video: {errorMessage}");
            }

            // Find the downloaded file
            string downloadedFile = result.Data;
            if (string.IsNullOrEmpty(downloadedFile) || !File.Exists(downloadedFile))
            {
                throw new InvalidOperationException("Downloaded file not found");
            }

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Successfully downloaded video to: {FilePath}", downloadedFile);
            }

            progress?.Report(1.0);

            return downloadedFile;
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error downloading video from {Url}", videoUrl);
            }
            throw;
        }
    }
}