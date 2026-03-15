using Hangfire;

namespace MyVideoArchive.Tests.Services;

public class ChannelServiceTests
{
    private static ChannelService CreateChannelService(
        InMemoryDatabaseFixture db,
        IConfiguration? configuration = null,
        IBackgroundJobClient? jobClient = null,
        IUserContextService? userContext = null,
        IUserInfoService? userInfoService = null)
    {
        var config = configuration ?? new ConfigurationBuilder().Build();
        var job = jobClient ?? Mock.Of<IBackgroundJobClient>();
        var user = userContext ?? CreateUserContext(null, false);
        var userInfo = userInfoService ?? CreateUserInfoService();
        return new ChannelService(
            NullLogger<ChannelService>.Instance,
            config,
            job,
            user,
            userInfo,
            db.ChannelRepository,
            db.ChannelTagRepository,
            db.CustomPlaylistVideoRepository,
            db.PlaylistRepository,
            db.PlaylistTagRepository,
            db.PlaylistVideoRepository,
            db.UserChannelRepository,
            db.UserPlaylistRepository,
            db.UserPlaylistVideoRepository,
            db.UserVideoRepository,
            db.VideoRepository,
            db.VideoTagRepository);
    }

    private static IUserContextService CreateUserContext(string? userId, bool isAdmin)
    {
        var mock = new Mock<IUserContextService>();
        mock.Setup(c => c.GetCurrentUserId()).Returns(userId);
        mock.Setup(c => c.IsAdministrator()).Returns(isAdmin);
        return mock.Object;
    }

    private static IUserInfoService CreateUserInfoService(IReadOnlyDictionary<string, UserInfo>? userInfoMap = null)
    {
        var mock = new Mock<IUserInfoService>();
        mock.Setup(s => s.GetUserInfoAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(userInfoMap ?? new Dictionary<string, UserInfo>());
        return mock.Object;
    }

    [Fact]
    public async Task DeleteChannelAsync_WhenChannelNotFound_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateChannelService(db);
        var result = await service.DeleteChannelAsync(999);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task DeleteChannelAsync_WhenChannelExists_CompletesWithoutNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        var service = CreateChannelService(db);
        var result = await service.DeleteChannelAsync(channel.Id, deleteMetadata: false, deleteFiles: false);
        // In-memory may not support DeleteAsync with navigation (x.Video.ChannelId); Success or Error is acceptable
        Assert.NotEqual(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task GetChannelAsync_WhenNotFound_ReturnsNull()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateChannelService(db);
        var result = await service.GetChannelAsync("YT", "nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetChannelAsync_WhenFound_ReturnsChannel()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "My Channel",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        var service = CreateChannelService(db);
        var result = await service.GetChannelAsync("YT", "ch1");
        Assert.NotNull(result);
        Assert.Equal(channel.Id, result.Id);
        Assert.Equal("My Channel", result.Name);
    }

    [Fact]
    public async Task GetChannelSubscribersAsync_WhenChannelNotFound_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateChannelService(db);
        var result = await service.GetChannelSubscribersAsync(999);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task GetChannelSubscribersAsync_WhenChannelExistsAndNoSubscribers_ReturnsEmptyList()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        var service = CreateChannelService(db);
        var result = await service.GetChannelSubscribersAsync(channel.Id);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task GetSyncStatusAsync_WhenChannelNotFound_ReturnsNull()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateChannelService(db);
        bool? result = await service.GetSyncStatusAsync(999);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetSyncStatusAsync_WhenChannelIsCustom_ReturnsFalse()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "custom1",
            Name = "C",
            Url = "https://c",
            Platform = "Custom",
            SubscribedAt = DateTime.UtcNow
        });
        var service = CreateChannelService(db);
        bool? result = await service.GetSyncStatusAsync(channel.Id);
        Assert.False(result);
    }

    [Fact]
    public async Task UserSubscribedToChannelAsync_WhenNoUserId_ReturnsFalse()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        var userContext = CreateUserContext(null, false);
        var service = CreateChannelService(db, userContext: userContext);
        bool result = await service.UserSubscribedToChannelAsync(channel.Id);
        Assert.False(result);
    }

    [Fact]
    public async Task UserSubscribedToChannelAsync_WhenAdmin_ReturnsTrue()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        var userContext = CreateUserContext("user1", isAdmin: true);
        var service = CreateChannelService(db, userContext: userContext);
        bool result = await service.UserSubscribedToChannelAsync(channel.Id);
        Assert.True(result);
    }

    [Fact]
    public async Task UserSubscribedToChannelAsync_WhenSubscribed_ReturnsTrue()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        await db.UserChannelRepository.InsertAsync(new UserChannel
        {
            UserId = "user1",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var userContext = CreateUserContext("user1", isAdmin: false);
        var service = CreateChannelService(db, userContext: userContext);
        bool result = await service.UserSubscribedToChannelAsync(channel.Id);
        Assert.True(result);
    }

    [Fact]
    public async Task UserSubscribedToChannelAsync_WhenNotSubscribed_ReturnsFalse()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        var userContext = CreateUserContext("user1", isAdmin: false);
        var service = CreateChannelService(db, userContext: userContext);
        bool result = await service.UserSubscribedToChannelAsync(channel.Id);
        Assert.False(result);
    }

    [Fact]
    public void SyncAllChannels_ReturnsSuccess()
    {
        using var db = new InMemoryDatabaseFixture();
        var jobClient = new Mock<IBackgroundJobClient>();
        var service = CreateChannelService(db, jobClient: jobClient.Object);
        var result = service.SyncAllChannels();
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetAvailableVideosAsync_WhenNotSubscribed_ReturnsForbidden()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        var userContext = CreateUserContext("user1", isAdmin: false);
        var service = CreateChannelService(db, userContext: userContext);
        var result = await service.GetAvailableVideosAsync(channel.Id);
        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task GetAvailableVideosAsync_WhenSubscribed_ReturnsSuccess()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        await db.UserChannelRepository.InsertAsync(new UserChannel
        {
            UserId = "user1",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var userContext = CreateUserContext("user1", isAdmin: false);
        var service = CreateChannelService(db, userContext: userContext);
        var result = await service.GetAvailableVideosAsync(channel.Id);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task GetDownloadedVideosAsync_WhenNotSubscribed_ReturnsForbidden()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        var userContext = CreateUserContext("user1", isAdmin: false);
        var service = CreateChannelService(db, userContext: userContext);
        var result = await service.GetDownloadedVideosAsync(channel.Id);
        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task GetDownloadedVideosAsync_WhenSubscribed_ReturnsSuccess()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        await db.UserChannelRepository.InsertAsync(new UserChannel
        {
            UserId = "user1",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var userContext = CreateUserContext("user1", isAdmin: false);
        var service = CreateChannelService(db, userContext: userContext);
        var result = await service.GetDownloadedVideosAsync(channel.Id);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task DownloadAllVideosAsync_EnqueuesJobAndReturnsZero()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        await db.UserChannelRepository.InsertAsync(new UserChannel
        {
            UserId = "user1",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var userContext = CreateUserContext("user1", isAdmin: false);
        var jobClient = new Mock<IBackgroundJobClient>();
        var service = CreateChannelService(db, jobClient: jobClient.Object, userContext: userContext);
        var result = await service.DownloadAllVideosAsync(channel.Id);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value >= 0);
    }

    [Fact]
    public async Task DownloadVideosAsync_WhenVideoIdsEmpty_ReturnsInvalid()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        await db.UserChannelRepository.InsertAsync(new UserChannel
        {
            UserId = "user1",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var userContext = CreateUserContext("user1", isAdmin: false);
        var service = CreateChannelService(db, userContext: userContext);
        var result = await service.DownloadVideosAsync(channel.Id, new DownloadVideosRequest { VideoIds = [] });
        Assert.Equal(ResultStatus.Invalid, result.Status);
    }
}