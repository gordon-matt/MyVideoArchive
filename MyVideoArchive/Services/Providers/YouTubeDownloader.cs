using MyVideoArchive.Services.Abstractions;
using System.Text.RegularExpressions;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace MyVideoArchive.Services.Providers;

/// <summary>
/// YouTube implementation of video downloader using yt-dlp
/// </summary>
public partial class YouTubeDownloader : IVideoDownloader
{
    private readonly ILogger<YouTubeDownloader> _logger;
    private readonly YoutubeDL _ytdl;
    private readonly IConfiguration _configuration;

    public string PlatformName => "YouTube";

    [GeneratedRegex(@"(youtube\.com|youtu\.be)", RegexOptions.IgnoreCase)]
    private static partial Regex YouTubeUrlRegex();

    public YouTubeDownloader(ILogger<YouTubeDownloader> logger, YoutubeDL youtubeDL, IConfiguration configuration)
    {
        _logger = logger;
        _ytdl = youtubeDL;
        _configuration = configuration;
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
            _logger.LogInformation("Downloading video from: {Url}", videoUrl);

            // Ensure output directory exists
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            // Get video quality from configuration
            var videoQuality = _configuration.GetValue<string>("VideoDownload:VideoQuality") 
                ?? "bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best";

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

            var result = await _ytdl.RunVideoDownload(videoUrl, overrideOptions: options, ct: cancellationToken);

            if (!result.Success)
            {
                var errorMessage = string.Join(", ", result.ErrorOutput);
                _logger.LogError("Failed to download video from {Url}: {Error}", videoUrl, errorMessage);
                throw new InvalidOperationException($"Failed to download video: {errorMessage}");
            }

            // Find the downloaded file
            var downloadedFile = result.Data;
            if (string.IsNullOrEmpty(downloadedFile) || !File.Exists(downloadedFile))
            {
                throw new InvalidOperationException("Downloaded file not found");
            }

            _logger.LogInformation("Successfully downloaded video to: {FilePath}", downloadedFile);
            progress?.Report(1.0);

            return downloadedFile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading video from {Url}", videoUrl);
            throw;
        }
    }
}