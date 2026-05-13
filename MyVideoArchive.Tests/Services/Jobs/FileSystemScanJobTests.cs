namespace MyVideoArchive.Tests.Services.Jobs;

public class FileSystemScanJobTests
{
    [Fact]
    public async Task ExecuteAsync_WhenDownloadPathMissing_ReturnsEmptyResult()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), "mva-missing-" + Guid.NewGuid().ToString("N"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["VideoDownload:OutputPath"] = missingPath })
            .Build();

        var job = new FileSystemScanJob(
            NullLogger<FileSystemScanJob>.Instance,
            configuration,
            Mock.Of<IRepository<Channel>>(),
            Mock.Of<IRepository<Video>>(),
            Mock.Of<IRepository<AdditionalContentItem>>(),
            Mock.Of<IRepository<Playlist>>(),
            Mock.Of<IRepository<Series>>(),
            Mock.Of<IRepository<SeriesPlaylist>>(),
            Mock.Of<IRepository<PlaylistVideo>>(),
            Mock.Of<IAdditionalContentService>(),
            new ThumbnailService(NullLogger<ThumbnailService>.Instance, Mock.Of<IHttpClientFactory>()));

        var result = await job.ExecuteAsync();

        Assert.Equal(0, result.NewVideos);
        Assert.Equal(0, result.UpdatedVideos);
    }
}