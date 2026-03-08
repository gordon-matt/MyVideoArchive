using Hangfire;
using MyVideoArchive.Models.Metadata;

namespace MyVideoArchive.Tests.Services.Jobs;

public class PlaylistSyncJobTests
{
    private static ILogger<PlaylistSyncJob> Logger => NullLogger<PlaylistSyncJob>.Instance;

    private static IConfiguration Configuration =>
        new ConfigurationBuilder().Build();

    [Fact]
    public async Task ExecuteAsync_WhenPlaylistNotFound_ReturnsWithoutThrowing()
    {
        using var db = new InMemoryDatabaseFixture();
        var job = CreateJob(db, factory: null, mockThumbnail: null);
        await job.ExecuteAsync(playlistId: 999);
        var playlists = await db.PlaylistRepository.FindAsync(new SearchOptions<Playlist> { });
        Assert.Empty(playlists);
    }

    [Fact]
    public async Task ExecuteAsync_WhenProviderIsNull_ReturnsWithoutThrowing()
    {
        using var db = new InMemoryDatabaseFixture();
        var (channel, playlist) = await SeedChannelAndPlaylistAsync(db, platform: "UnknownPlatform");
        var factory = new VideoMetadataProviderFactory(Array.Empty<IVideoMetadataProvider>(),
            NullLogger<VideoMetadataProviderFactory>.Instance);
        var job = CreateJob(db, factory, mockThumbnail: null);
        await job.ExecuteAsync(playlist.Id);
        var loaded = await db.PlaylistRepository.FindOneAsync(new SearchOptions<Playlist> { Query = x => x.Id == playlist.Id });
        Assert.NotNull(loaded);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoSubscribers_UpdatesPlaylistLastChecked()
    {
        using var db = new InMemoryDatabaseFixture();
        var (channel, playlist) = await SeedChannelAndPlaylistAsync(db, platform: "YouTube");
        Assert.Null(playlist.LastChecked);
        var provider = new FakeMetadataProvider("YouTube", playlist.Url,
            playlistMetadata: new PlaylistMetadata
            {
                PlaylistId = playlist.PlaylistId,
                Name = playlist.Name,
                Url = playlist.Url,
                Platform = "YouTube",
                ChannelId = channel.ChannelId,
                ChannelName = channel.Name
            },
            playlistVideos: []);
        var factory = new VideoMetadataProviderFactory(
            new[] { provider },
            NullLogger<VideoMetadataProviderFactory>.Instance);
        var job = CreateJob(db, factory, mockThumbnail: null);
        await job.ExecuteAsync(playlist.Id);
        var updated = await db.PlaylistRepository.FindOneAsync(new SearchOptions<Playlist>
        { Query = x => x.Id == playlist.Id });
        Assert.NotNull(updated);
        Assert.NotNull(updated.LastChecked);
    }

    [Fact]
    public async Task ExecuteAsync_WhenProviderReturnsDuplicateVideoIds_InsertsOnlyOneVideoPerKey()
    {
        using var db = new InMemoryDatabaseFixture();
        var (channel, playlist) = await SeedChannelAndPlaylistWithSubscriberAsync(db, platform: "YouTube");
        var sameVideo = new VideoMetadata
        {
            VideoId = "vid1",
            Title = "Title",
            Platform = "YouTube",
            Url = "https://example.com/vid1",
            ChannelId = channel.ChannelId,
            ChannelName = channel.Name
        };
        var provider = new FakeMetadataProvider("YouTube", playlist.Url,
            playlistMetadata: null,
            playlistVideos: [sameVideo, sameVideo]);
        var factory = new VideoMetadataProviderFactory(new[] { provider },
            NullLogger<VideoMetadataProviderFactory>.Instance);
        var thumbnailService = new ThumbnailService(NullLogger<ThumbnailService>.Instance, Mock.Of<IHttpClientFactory>());
        var job = CreateJob(db, factory, thumbnailService);
        await job.ExecuteAsync(playlist.Id);
        var videos = await db.VideoRepository.FindAsync(new SearchOptions<Video>
        { Query = x => x.Platform == "YouTube" && x.VideoId == "vid1" });
        Assert.Single(videos);
    }

    [Fact]
    public async Task ExecuteAsync_WhenInsertThrowsDuplicateKey_ResolvesExistingAndLinksPlaylist()
    {
        using var db = new InMemoryDatabaseFixture();
        var (channel, playlist) = await SeedChannelAndPlaylistWithSubscriberAsync(db, platform: "YouTube");
        var existingVideo = new Video
        {
            VideoId = "existing-vid",
            Title = "Existing",
            Platform = "YouTube",
            Url = "https://example.com/existing",
            ChannelId = channel.Id,
            IsIgnored = false,
            IsQueued = false,
            NeedsMetadataReview = false
        };
        await db.VideoRepository.InsertAsync(existingVideo);
        var newVideoMeta = new VideoMetadata
        {
            VideoId = "existing-vid",
            Title = "Updated Title",
            Platform = "YouTube",
            Url = "https://example.com/existing",
            ChannelId = channel.ChannelId,
            ChannelName = channel.Name
        };
        var provider = new FakeMetadataProvider("YouTube", playlist.Url, null, [newVideoMeta]);
        var factory = new VideoMetadataProviderFactory(new[] { provider },
            NullLogger<VideoMetadataProviderFactory>.Instance);
        var thumbnailService = new ThumbnailService(NullLogger<ThumbnailService>.Instance, Mock.Of<IHttpClientFactory>());
        var videoRepoMock = new Mock<IRepository<Video>>();
        int findCallCount = 0;
        videoRepoMock.Setup(r => r.FindOneAsync(It.IsAny<SearchOptions<Video>>()))
            .ReturnsAsync(() => ++findCallCount == 1 ? null : existingVideo);
        bool firstBulkInsert = true;
        videoRepoMock.Setup(r => r.InsertAsync(It.IsAny<IEnumerable<Video>>(), It.IsAny<ContextOptions>()))
            .ReturnsAsync((IEnumerable<Video> v, ContextOptions _) =>
            {
                if (firstBulkInsert)
                {
                    firstBulkInsert = false;
                    throw new DbUpdateException("23505", new Exception("23505: duplicate key"));
                }
                return v;
            });
        videoRepoMock.Setup(r => r.InsertAsync(It.IsAny<Video>(), It.IsAny<ContextOptions>()))
            .ReturnsAsync((Video v, ContextOptions _) => (Video?)v);
        videoRepoMock.Setup(r => r.UpdateAsync(It.IsAny<IEnumerable<Video>>(), It.IsAny<ContextOptions>()))
            .ReturnsAsync((IEnumerable<Video> v, ContextOptions _) => v);
        var job = new PlaylistSyncJob(Logger, Configuration, factory, thumbnailService,
            Mock.Of<IBackgroundJobClient>(),
            db.PlaylistRepository, db.PlaylistVideoRepository, db.UserPlaylistRepository,
            videoRepoMock.Object);
        await job.ExecuteAsync(playlist.Id);
        videoRepoMock.Verify(r => r.UpdateAsync(It.IsAny<IEnumerable<Video>>(), It.IsAny<ContextOptions>()), Times.AtLeastOnce);
        var links = await db.PlaylistVideoRepository.FindAsync(new SearchOptions<PlaylistVideo>
        { Query = x => x.PlaylistId == playlist.Id && x.VideoId == existingVideo.Id });
        Assert.NotEmpty(links);
    }

    private static PlaylistSyncJob CreateJob(
        InMemoryDatabaseFixture db,
        VideoMetadataProviderFactory? factory = null,
        ThumbnailService? mockThumbnail = null)
    {
        factory ??= new VideoMetadataProviderFactory(Array.Empty<IVideoMetadataProvider>(),
            NullLogger<VideoMetadataProviderFactory>.Instance);
        mockThumbnail ??= new Mock<ThumbnailService>(NullLogger<ThumbnailService>.Instance, Mock.Of<IHttpClientFactory>()).Object;
        return new PlaylistSyncJob(Logger, Configuration, factory, mockThumbnail,
            Mock.Of<IBackgroundJobClient>(),
            db.PlaylistRepository, db.PlaylistVideoRepository, db.UserPlaylistRepository,
            db.VideoRepository);
    }

    private static async Task<(Channel Channel, Playlist Playlist)> SeedChannelAndPlaylistAsync(InMemoryDatabaseFixture db, string platform)
    {
        var channel = new Channel
        {
            ChannelId = "ch1",
            Name = "Channel 1",
            Url = "https://example.com/ch1",
            Platform = platform,
            SubscribedAt = DateTime.UtcNow
        };
        await db.ChannelRepository.InsertAsync(channel);
        var playlist = new Playlist
        {
            PlaylistId = "pl1",
            Name = "Playlist 1",
            Url = "https://example.com/pl1",
            Platform = platform,
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        };
        await db.PlaylistRepository.InsertAsync(playlist);
        return (channel, playlist);
    }

    private static async Task<(Channel Channel, Playlist Playlist)> SeedChannelAndPlaylistWithSubscriberAsync(InMemoryDatabaseFixture db, string platform)
    {
        var (channel, playlist) = await SeedChannelAndPlaylistAsync(db, platform);
        using var ctx = db.CreateContext();
        var user = new ApplicationUser { UserName = "test", Email = "test@test.com" };
        ctx.Set<ApplicationUser>().Add(user);
        await ctx.SaveChangesAsync();
        await db.UserPlaylistRepository.InsertAsync(new UserPlaylist
        {
            UserId = user.Id,
            PlaylistId = playlist.Id,
            SubscribedAt = DateTime.UtcNow
        });
        return (channel, playlist);
    }

    private sealed class FakeMetadataProvider : IVideoMetadataProvider
    {
        private readonly string _playlistUrl;
        private readonly PlaylistMetadata? _playlistMetadata;
        private readonly List<VideoMetadata> _playlistVideos;

        public FakeMetadataProvider(string platform, string playlistUrl,
            PlaylistMetadata? playlistMetadata, List<VideoMetadata> playlistVideos)
        {
            PlatformName = platform;
            _playlistUrl = playlistUrl;
            _playlistMetadata = playlistMetadata;
            _playlistVideos = playlistVideos;
        }

        public string PlatformName { get; }

        public bool CanHandle(string url) => url.Contains(_playlistUrl, StringComparison.OrdinalIgnoreCase);

        public string BuildChannelUrl(string channelId) => $"https://example.com/channel/{channelId}";

        public Task<ChannelMetadata?> GetChannelMetadataAsync(string channelUrl, CancellationToken cancellationToken = default) => Task.FromResult<ChannelMetadata?>(null);

        public Task<VideoMetadata?> GetVideoMetadataAsync(string videoUrl, CancellationToken cancellationToken = default) => Task.FromResult<VideoMetadata?>(null);

        public Task<PlaylistMetadata?> GetPlaylistMetadataAsync(string playlistUrl, CancellationToken cancellationToken = default) =>
            Task.FromResult(playlistUrl == _playlistUrl ? _playlistMetadata : null);

        public Task<List<VideoMetadata>> GetPlaylistVideosAsync(string playlistUrl, CancellationToken cancellationToken = default) =>
            Task.FromResult(playlistUrl == _playlistUrl ? _playlistVideos : []);

        public Task<List<VideoMetadata>> GetChannelVideosAsync(string channelUrl, CancellationToken cancellationToken = default) => Task.FromResult(new List<VideoMetadata>());

        public Task<List<PlaylistMetadata>> GetChannelPlaylistsAsync(string channelUrl, CancellationToken cancellationToken = default) => Task.FromResult(new List<PlaylistMetadata>());
    }
}