namespace MyVideoArchive.Tests;

public class VideoDownloaderFactoryTests
{
    [Fact]
    public void GetDownloader_WhenNoDownloaders_ReturnsNull()
    {
        var factory = new VideoDownloaderFactory(
            Array.Empty<IVideoDownloader>(),
            NullLogger<VideoDownloaderFactory>.Instance);

        var result = factory.GetDownloader("https://youtube.com/watch?v=abc");

        Assert.Null(result);
    }

    [Fact]
    public void GetDownloader_WhenDownloaderCanHandle_ReturnsDownloader()
    {
        var downloader = new StubDownloader("YouTube") { HandlesUrl = true };
        var factory = new VideoDownloaderFactory(
            new[] { downloader },
            NullLogger<VideoDownloaderFactory>.Instance);

        var result = factory.GetDownloader("https://youtube.com/watch?v=abc");

        Assert.Same(downloader, result);
    }

    [Fact]
    public void GetDownloaderByPlatform_WhenNoMatch_ReturnsNull()
    {
        var downloader = new StubDownloader("YouTube");
        var factory = new VideoDownloaderFactory(
            new[] { downloader },
            NullLogger<VideoDownloaderFactory>.Instance);

        var result = factory.GetDownloaderByPlatform("Vimeo");

        Assert.Null(result);
    }

    [Fact]
    public void GetDownloaderByPlatform_WhenMatch_ReturnsDownloader()
    {
        var downloader = new StubDownloader("YouTube");
        var factory = new VideoDownloaderFactory(
            new[] { downloader },
            NullLogger<VideoDownloaderFactory>.Instance);

        var result = factory.GetDownloaderByPlatform("YouTube");

        Assert.Same(downloader, result);
    }

    [Fact]
    public void GetDownloaderByPlatform_WhenMatchCaseInsensitive_ReturnsDownloader()
    {
        var downloader = new StubDownloader("YouTube");
        var factory = new VideoDownloaderFactory(
            new[] { downloader },
            NullLogger<VideoDownloaderFactory>.Instance);

        var result = factory.GetDownloaderByPlatform("youtube");

        Assert.Same(downloader, result);
    }

    private sealed class StubDownloader : IVideoDownloader
    {
        public StubDownloader(string platformName)
        {
            PlatformName = platformName;
        }

        public string PlatformName { get; }
        public bool HandlesUrl { get; set; }

        public bool CanHandle(string url) => HandlesUrl;

        public Task<string> DownloadVideoAsync(string videoUrl, string outputPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult("");
    }
}