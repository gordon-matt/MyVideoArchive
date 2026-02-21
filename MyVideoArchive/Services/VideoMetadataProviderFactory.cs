using MyVideoArchive.Services.Abstractions;

namespace MyVideoArchive.Services;

/// <summary>
/// Factory service to route to the appropriate metadata provider based on URL
/// </summary>
public class VideoMetadataProviderFactory
{
    private readonly IEnumerable<IVideoMetadataProvider> providers;
    private readonly ILogger<VideoMetadataProviderFactory> logger;

    public VideoMetadataProviderFactory(
        IEnumerable<IVideoMetadataProvider> providers,
        ILogger<VideoMetadataProviderFactory> logger)
    {
        this.providers = providers;
        this.logger = logger;
    }

    public IVideoMetadataProvider? GetProvider(string url)
    {
        var provider = providers.FirstOrDefault(p => p.CanHandle(url));

        if (provider == null)
        {
            logger.LogWarning("No metadata provider found for URL: {Url}", url);
        }

        return provider;
    }

    public IVideoMetadataProvider? GetProviderByPlatform(string platform)
    {
        var provider = providers.FirstOrDefault(p =>
            p.PlatformName.Equals(platform, StringComparison.OrdinalIgnoreCase));

        if (provider == null)
        {
            logger.LogWarning("No metadata provider found for platform: {Platform}", platform);
        }

        return provider;
    }
}