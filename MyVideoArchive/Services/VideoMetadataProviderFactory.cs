using MyVideoArchive.Services.Abstractions;

namespace MyVideoArchive.Services;

/// <summary>
/// Factory service to route to the appropriate metadata provider based on URL
/// </summary>
public class VideoMetadataProviderFactory
{
    private readonly IEnumerable<IVideoMetadataProvider> _providers;
    private readonly ILogger<VideoMetadataProviderFactory> _logger;

    public VideoMetadataProviderFactory(
        IEnumerable<IVideoMetadataProvider> providers,
        ILogger<VideoMetadataProviderFactory> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    public IVideoMetadataProvider? GetProvider(string url)
    {
        var provider = _providers.FirstOrDefault(p => p.CanHandle(url));

        if (provider == null)
        {
            _logger.LogWarning("No metadata provider found for URL: {Url}", url);
        }

        return provider;
    }

    public IVideoMetadataProvider? GetProviderByPlatform(string platform)
    {
        var provider = _providers.FirstOrDefault(p => 
            p.PlatformName.Equals(platform, StringComparison.OrdinalIgnoreCase));

        if (provider == null)
        {
            _logger.LogWarning("No metadata provider found for platform: {Platform}", platform);
        }

        return provider;
    }
}
