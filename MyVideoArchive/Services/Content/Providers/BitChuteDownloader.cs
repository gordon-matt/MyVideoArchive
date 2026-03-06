using System.Text.RegularExpressions;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace MyVideoArchive.Services.Content.Providers;

/// <summary>
/// BitChute implementation of video downloader using yt-dlp
/// </summary>
public partial class BitChuteDownloader : IVideoDownloader
{
    private readonly ILogger<BitChuteDownloader> logger;
    private readonly IConfiguration configuration;
    private readonly YoutubeDL ytdl;

    public string PlatformName => "BitChute";

    [GeneratedRegex(@"bitchute\.com", RegexOptions.IgnoreCase)]
    private static partial Regex BitChuteUrlRegex();

    public BitChuteDownloader(
        ILogger<BitChuteDownloader> logger,
        IConfiguration configuration,
        YoutubeDL ytdl)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.ytdl = ytdl;
    }

    public bool CanHandle(string url) => BitChuteUrlRegex().IsMatch(url);

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
                logger.LogInformation("Downloading BitChute video from: {Url}", videoUrl);
            }

            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            string videoQuality = configuration.GetValue<string>("VideoDownload:VideoQuality")
                ?? Constants.BestDownloadQuality;

            var options = new OptionSet
            {
                Format = videoQuality,
                MergeOutputFormat = DownloadMergeFormat.Mp4,
                Output = Path.Combine(outputPath, "%(id)s.%(ext)s"),
                WriteInfoJson = true,
                WriteThumbnail = true,
                EmbedThumbnail = true,
                EmbedMetadata = true,
                NoPlaylist = true
            };

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
                        logger.LogWarning("Failed to download BitChute video from {Url}. Attempting fallback to best quality", videoUrl);
                    }

                    options.Format = Constants.BestDownloadQuality;
                    result = await ytdl.RunVideoDownload(videoUrl, overrideOptions: options, ct: cancellationToken);
                }
            }

            if (!result!.Success)
            {
                string errorMessage = string.Join(", ", result.ErrorOutput);
                if (logger.IsEnabled(LogLevel.Error))
                {
                    logger.LogError("Failed to download BitChute video from {Url}: {Error}", videoUrl, errorMessage);
                }

                throw new InvalidOperationException($"Failed to download video: {errorMessage}");
            }

            string downloadedFile = result.Data;
            if (string.IsNullOrEmpty(downloadedFile) || !File.Exists(downloadedFile))
            {
                throw new InvalidOperationException("Downloaded file not found");
            }

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Successfully downloaded BitChute video to: {FilePath}", downloadedFile);
            }

            progress?.Report(1.0);

            return downloadedFile;
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error downloading BitChute video from {Url}", videoUrl);
            }
            throw;
        }
    }
}
