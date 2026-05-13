using System.Net;
using System.Net.Http.Headers;

namespace MyVideoArchive.Tests.Services;

public class ThumbnailServiceTests
{
    [Theory]
    [InlineData("/archive/foo/bar.jpg", true)]
    [InlineData("https://cdn.example/x.png", false)]
    [InlineData(null, false)]
    public void IsLocalUrl_ClassifiesPaths(string? url, bool expected) =>
        Assert.Equal(expected, ThumbnailService.IsLocalUrl(url));

    [Theory]
    [InlineData("https://x/y", true)]
    [InlineData("http://x/y", true)]
    [InlineData("/archive/x", false)]
    [InlineData(null, false)]
    public void IsRemoteUrl_ClassifiesUrls(string? url, bool expected) =>
        Assert.Equal(expected, ThumbnailService.IsRemoteUrl(url));

    [Fact]
    public void BuildRelativeUrl_EncodesSegmentsAndUsesArchivePrefix()
    {
        string basePath = @"D:\Downloads";
        string filePath = @"D:\Downloads\chan#1\a b.jpg";
        string url = ThumbnailService.BuildRelativeUrl(basePath, filePath);
        Assert.StartsWith(ThumbnailService.ArchiveUrlPrefix, url, StringComparison.Ordinal);
        Assert.Contains("%23", url, StringComparison.Ordinal);
        Assert.Contains("%20", url, StringComparison.Ordinal);
    }

    private sealed class StubThumbnailHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4])
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task DownloadAndSaveAsync_WritesBytesAndReturnsArchiveUrl()
    {
        string downloadRoot = Path.Combine(Path.GetTempPath(), "mva-thumb-" + Guid.NewGuid().ToString("N"));
        string saveDir = Path.Combine(downloadRoot, "ch1");
        Directory.CreateDirectory(downloadRoot);
        try
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient("thumbnails")).Returns(new HttpClient(new StubThumbnailHandler()));
            var service = new ThumbnailService(NullLogger<ThumbnailService>.Instance, factory.Object);

            string? url = await service.DownloadAndSaveAsync(
                "https://example.com/thumb.png",
                saveDir,
                "thumb",
                downloadRoot);

            Assert.NotNull(url);
            Assert.True(ThumbnailService.IsLocalUrl(url));
            Assert.True(File.Exists(Path.Combine(saveDir, "thumb.png")));
        }
        finally
        {
            try { Directory.Delete(downloadRoot, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task DownloadAndSaveAsync_WhenUrlEmpty_ReturnsNull()
    {
        var factory = new Mock<IHttpClientFactory>();
        var service = new ThumbnailService(NullLogger<ThumbnailService>.Instance, factory.Object);
        Assert.Null(await service.DownloadAndSaveAsync(null, Path.GetTempPath(), "x", Path.GetTempPath()));
        factory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GenerateFromVideoAsync_WhenFileMissing_ReturnsNull()
    {
        var factory = new Mock<IHttpClientFactory>();
        var service = new ThumbnailService(NullLogger<ThumbnailService>.Instance, factory.Object);
        string? url = await service.GenerateFromVideoAsync(
            Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".mp4"),
            Path.GetTempPath(),
            "out",
            Path.GetTempPath());
        Assert.Null(url);
    }
}