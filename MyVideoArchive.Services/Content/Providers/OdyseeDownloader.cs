using System.Text.RegularExpressions;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace MyVideoArchive.Services.Content.Providers;

/// <summary>
/// Odysee implementation of video downloader using yt-dlp (LBRY extractor)
/// </summary>
public partial class OdyseeDownloader : IVideoDownloader
{
    private readonly ILogger<OdyseeDownloader> logger;
    private readonly IConfiguration configuration;
    private readonly YoutubeDL ytdl;

    public string PlatformName => "Odysee";

    [GeneratedRegex(@"odysee\.com", RegexOptions.IgnoreCase)]
    private static partial Regex OdyseeUrlRegex();

    public OdyseeDownloader(
        ILogger<OdyseeDownloader> logger,
        IConfiguration configuration,
        YoutubeDL ytdl)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.ytdl = ytdl;
    }

    public bool CanHandle(string url) => OdyseeUrlRegex().IsMatch(url);

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
                logger.LogInformation("Downloading Odysee video from: {Url}", videoUrl);
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

            OdyseeRateLimitOptions.Apply(options, configuration, logger);

            SubtitleOptionsExtensions.ApplySubtitleOptions(options, configuration);

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
                        logger.LogWarning("Failed to download Odysee video from {Url}. Attempting fallback to best quality", videoUrl);
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
                    logger.LogError("Failed to download Odysee video from {Url}: {Error}", videoUrl, errorMessage);
                }

                if (TransientDownloadException.IsTransientError(errorMessage))
                {
                    throw new TransientDownloadException($"Odysee rate limited the download (will retry): {errorMessage}");
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
                logger.LogInformation("Successfully downloaded Odysee video to: {FilePath}", downloadedFile);
            }

            progress?.Report(1.0);

            return downloadedFile;
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error downloading Odysee video from {Url}", videoUrl);
            }
            throw;
        }
    }

    public async Task<bool> DownloadSubtitlesAsync(
        string videoUrl,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await SubtitleOptionsExtensions.RunSubtitleOnlyDownloadAsync(
                ytdl, logger, configuration, videoUrl, outputPath, cancellationToken,
                options => OdyseeRateLimitOptions.Apply(options, configuration, logger));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error downloading Odysee subtitles from {Url}", videoUrl);
            }
            return false;
        }
    }
}
