using MyVideoArchive.Data;

namespace MyVideoArchive.Tests.TestHelpers;

/// <summary>
/// Provides an in-memory ApplicationDbContext and Extenso repositories for unit tests.
/// Use a unique database name per test to avoid cross-test pollution.
/// </summary>
public sealed class InMemoryDatabaseFixture : IDisposable
{
    private readonly string _databaseName;
    private readonly DbContextOptions<ApplicationDbContext> _options;
    private readonly TestDbContextFactory _factory;

    public InMemoryDatabaseFixture(string? databaseName = null)
    {
        _databaseName = databaseName ?? Guid.NewGuid().ToString();
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .Options;
        _factory = new TestDbContextFactory(_options);
        ChannelRepository = new EntityFrameworkRepository<Channel>(_factory);
        ChannelTagRepository = new EntityFrameworkRepository<ChannelTag>(_factory);
        CustomPlaylistRepository = new EntityFrameworkRepository<CustomPlaylist>(_factory);
        CustomPlaylistTagRepository = new EntityFrameworkRepository<CustomPlaylistTag>(_factory);
        CustomPlaylistVideoRepository = new EntityFrameworkRepository<CustomPlaylistVideo>(_factory);
        PlaylistRepository = new EntityFrameworkRepository<Playlist>(_factory);
        PlaylistTagRepository = new EntityFrameworkRepository<PlaylistTag>(_factory);
        PlaylistVideoRepository = new EntityFrameworkRepository<PlaylistVideo>(_factory);
        TagRepository = new EntityFrameworkRepository<Tag>(_factory);
        UserChannelRepository = new EntityFrameworkRepository<UserChannel>(_factory);
        UserPlaylistRepository = new EntityFrameworkRepository<UserPlaylist>(_factory);
        UserPlaylistVideoRepository = new EntityFrameworkRepository<UserPlaylistVideo>(_factory);
        UserSettingsRepository = new EntityFrameworkRepository<UserSettingsEntry>(_factory);
        UserVideoRepository = new EntityFrameworkRepository<UserVideo>(_factory);
        VideoRepository = new EntityFrameworkRepository<Video>(_factory);
        VideoTagRepository = new EntityFrameworkRepository<VideoTag>(_factory);
    }

    public IRepository<Channel> ChannelRepository { get; }
    public IRepository<ChannelTag> ChannelTagRepository { get; }
    public IRepository<CustomPlaylist> CustomPlaylistRepository { get; }
    public IRepository<CustomPlaylistTag> CustomPlaylistTagRepository { get; }
    public IRepository<CustomPlaylistVideo> CustomPlaylistVideoRepository { get; }
    public IRepository<Playlist> PlaylistRepository { get; }
    public IRepository<PlaylistTag> PlaylistTagRepository { get; }
    public IRepository<PlaylistVideo> PlaylistVideoRepository { get; }
    public IRepository<Tag> TagRepository { get; }
    public IRepository<UserChannel> UserChannelRepository { get; }
    public IRepository<UserPlaylist> UserPlaylistRepository { get; }
    public IRepository<UserPlaylistVideo> UserPlaylistVideoRepository { get; }
    public IRepository<UserSettingsEntry> UserSettingsRepository { get; }
    public IRepository<UserVideo> UserVideoRepository { get; }
    public IRepository<Video> VideoRepository { get; }
    public IRepository<VideoTag> VideoTagRepository { get; }

    /// <summary>
    /// Get a context instance for seeding data or assertions. Call SaveChangesAsync after changes.
    /// </summary>
    public ApplicationDbContext CreateContext() => new(_options);

    public void Dispose()
    { }

    private sealed class TestDbContextFactory : IDbContextFactory
    {
        private readonly DbContextOptions<ApplicationDbContext> _options;

        public TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        {
            _options = options;
        }

        public DbContext GetContext() => new ApplicationDbContext(_options);

        public DbContext GetContext(string connectionString) => GetContext();
    }
}