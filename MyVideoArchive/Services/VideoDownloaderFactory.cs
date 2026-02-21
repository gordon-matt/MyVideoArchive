using MyVideoArchive.Services.Abstractions;

namespace MyVideoArchive.Services;

/// <summary>
/// Factory service to route to the appropriate video downloader based on URL
/// </summary>
public class VideoDownloaderFactory
{
    private readonly IEnumerable<IVideoDownloader> downloaders;
    private readonly ILogger<VideoDownloaderFactory> logger;

    public VideoDownloaderFactory(
        IEnumerable<IVideoDownloader> downloaders,
        ILogger<VideoDownloaderFactory> logger)
    {
        this.downloaders = downloaders;
        this.logger = logger;
    }

    public IVideoDownloader? GetDownloader(string url)
    {
        var downloader = downloaders.FirstOrDefault(d => d.CanHandle(url));

        if (downloader == null)
        {
            logger.LogWarning("No downloader found for URL: {Url}", url);
        }

        return downloader;
    }

    public IVideoDownloader? GetDownloaderByPlatform(string platform)
    {
        var downloader = downloaders.FirstOrDefault(d =>
            d.PlatformName.Equals(platform, StringComparison.OrdinalIgnoreCase));

        if (downloader == null)
        {
            logger.LogWarning("No downloader found for platform: {Platform}", platform);
        }

        return downloader;
    }
}