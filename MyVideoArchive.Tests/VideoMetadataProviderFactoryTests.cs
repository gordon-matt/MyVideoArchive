using Microsoft.Extensions.Logging.Abstractions;
using MyVideoArchive.Services.Content;
using MyVideoArchive.Services.Content.Providers;

namespace MyVideoArchive.Tests;

public class VideoMetadataProviderFactoryTests
{
    [Fact]
    public void GetProviderByPlatform_WhenNoProviders_ReturnsNull()
    {
        var factory = new VideoMetadataProviderFactory(
            Array.Empty<IVideoMetadataProvider>(),
            NullLogger<VideoMetadataProviderFactory>.Instance);

        var result = factory.GetProviderByPlatform("YouTube");

        Assert.Null(result);
    }

    [Fact]
    public void GetProviderByPlatform_WhenProviderMatches_ReturnsProvider()
    {
        var provider = new StubProvider("YouTube");
        var factory = new VideoMetadataProviderFactory(
            new[] { provider },
            NullLogger<VideoMetadataProviderFactory>.Instance);

        var result = factory.GetProviderByPlatform("YouTube");

        Assert.Same(provider, result);
    }

    [Fact]
    public void GetProviderByPlatform_WhenProviderMatchesCaseInsensitive_ReturnsProvider()
    {
        var provider = new StubProvider("YouTube");
        var factory = new VideoMetadataProviderFactory(
            new[] { provider },
            NullLogger<VideoMetadataProviderFactory>.Instance);

        var result = factory.GetProviderByPlatform("youtube");

        Assert.Same(provider, result);
    }

    [Fact]
    public void GetProviderByPlatform_WhenNoMatch_ReturnsNull()
    {
        var provider = new StubProvider("YouTube");
        var factory = new VideoMetadataProviderFactory(
            new[] { provider },
            NullLogger<VideoMetadataProviderFactory>.Instance);

        var result = factory.GetProviderByPlatform("Vimeo");

        Assert.Null(result);
    }

    [Fact]
    public void GetProvider_WhenNoProviders_ReturnsNull()
    {
        var factory = new VideoMetadataProviderFactory(
            Array.Empty<IVideoMetadataProvider>(),
            NullLogger<VideoMetadataProviderFactory>.Instance);

        var result = factory.GetProvider("https://youtube.com/playlist?list=abc");

        Assert.Null(result);
    }

    [Fact]
    public void GetProvider_WhenProviderCanHandleUrl_ReturnsProvider()
    {
        var provider = new StubProvider("YouTube") { HandlesUrl = true };
        var factory = new VideoMetadataProviderFactory(
            new[] { provider },
            NullLogger<VideoMetadataProviderFactory>.Instance);

        var result = factory.GetProvider("https://youtube.com/playlist?list=abc");

        Assert.Same(provider, result);
    }

    [Fact]
    public void GetProvider_WhenNoProviderCanHandle_ReturnsNull()
    {
        var provider = new StubProvider("YouTube") { HandlesUrl = false };
        var factory = new VideoMetadataProviderFactory(
            new[] { provider },
            NullLogger<VideoMetadataProviderFactory>.Instance);

        var result = factory.GetProvider("https://unknown.com/video");

        Assert.Null(result);
    }

    private sealed class StubProvider : IVideoMetadataProvider
    {
        public StubProvider(string platformName)
        {
            PlatformName = platformName;
        }

        public string PlatformName { get; }

        /// <summary>When set, CanHandle(url) returns this value.</summary>
        public bool HandlesUrl { get; set; }

        public bool CanHandle(string url) => HandlesUrl;

        public string BuildChannelUrl(string channelId) => "";

        public Task<Models.Metadata.ChannelMetadata?> GetChannelMetadataAsync(string channelUrl, CancellationToken cancellationToken = default) => Task.FromResult<Models.Metadata.ChannelMetadata?>(null);

        public Task<Models.Metadata.VideoMetadata?> GetVideoMetadataAsync(string videoUrl, CancellationToken cancellationToken = default) => Task.FromResult<Models.Metadata.VideoMetadata?>(null);

        public Task<Models.Metadata.PlaylistMetadata?> GetPlaylistMetadataAsync(string playlistUrl, CancellationToken cancellationToken = default) => Task.FromResult<Models.Metadata.PlaylistMetadata?>(null);

        public Task<List<Models.Metadata.VideoMetadata>> GetPlaylistVideosAsync(string playlistUrl, CancellationToken cancellationToken = default) => Task.FromResult(new List<Models.Metadata.VideoMetadata>());

        public Task<List<Models.Metadata.VideoMetadata>> GetChannelVideosAsync(string channelUrl, CancellationToken cancellationToken = default) => Task.FromResult(new List<Models.Metadata.VideoMetadata>());

        public Task<List<Models.Metadata.PlaylistMetadata>> GetChannelPlaylistsAsync(string channelUrl, CancellationToken cancellationToken = default) => Task.FromResult(new List<Models.Metadata.PlaylistMetadata>());
    }
}