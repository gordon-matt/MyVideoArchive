using Hangfire;
using MyVideoArchive.Models.Requests.Playlist;

namespace MyVideoArchive.Tests.Services;

public class PlaylistServiceTests
{
    private static PlaylistService CreatePlaylistService(
        InMemoryDatabaseFixture db,
        IUserContextService? userContext = null,
        IBackgroundJobClient? jobClient = null,
        VideoMetadataProviderFactory? metadataFactory = null)
    {
        var user = userContext ?? CreateUserContext("user1", false);
        var job = jobClient ?? Mock.Of<IBackgroundJobClient>();
        var factory = metadataFactory ?? new VideoMetadataProviderFactory(
            [],
            NullLogger<VideoMetadataProviderFactory>.Instance);
        var thumbnail = new ThumbnailService(
            NullLogger<ThumbnailService>.Instance,
            Mock.Of<IHttpClientFactory>());

        var tagSvc = new TagService(
            NullLogger<TagService>.Instance,
            user,
            db.TagRepository,
            db.VideoRepository,
            db.VideoTagRepository,
            db.ChannelTagRepository,
            db.PlaylistTagRepository);

        return new PlaylistService(
            NullLogger<PlaylistService>.Instance,
            new ConfigurationBuilder().Build(),
            user,
            job,
            factory,
            thumbnail,
            tagSvc,
            db.ChannelRepository,
            db.PlaylistRepository,
            db.PlaylistVideoRepository,
            db.UserChannelRepository,
            db.UserPlaylistRepository,
            db.UserPlaylistVideoRepository,
            db.VideoRepository,
            db.UserVideoRepository);
    }

    private static IUserContextService CreateUserContext(string? userId, bool isAdmin)
    {
        var mock = new Mock<IUserContextService>();
        mock.Setup(c => c.GetCurrentUserId()).Returns(userId);
        mock.Setup(c => c.IsAdministrator()).Returns(isAdmin);
        return mock.Object;
    }

    [Fact]
    public async Task GetAvailablePlaylistsAsync_WhenNoAccess_ReturnsForbidden()
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
        var userContext = CreateUserContext("user1", false);
        var service = CreatePlaylistService(db, userContext: userContext);
        var result = await service.GetAvailablePlaylistsAsync(channel.Id);
        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task GetAvailablePlaylistsAsync_WhenChannelNotFound_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var userContext = CreateUserContext("user1", true);
        var service = CreatePlaylistService(db, userContext: userContext);
        var result = await service.GetAvailablePlaylistsAsync(999);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task GetCustomOrderAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreatePlaylistService(db, userContext: CreateUserContext(null, false));
        var result = await service.GetCustomOrderAsync(1);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task GetCustomOrderAsync_WhenUser_ReturnsSuccessWithEmptyOrder()
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
        var playlist = await db.PlaylistRepository.InsertAsync(new Playlist
        {
            PlaylistId = "pl1",
            Name = "P",
            Url = "https://p",
            Platform = "YT",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var service = CreatePlaylistService(db);
        var result = await service.GetCustomOrderAsync(playlist.Id);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value.VideoOrders);
    }

    [Fact]
    public async Task GetOrderSettingAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreatePlaylistService(db, userContext: CreateUserContext(null, false));
        var result = await service.GetOrderSettingAsync(1);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task GetOrderSettingAsync_WhenUser_ReturnsSuccess()
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
        var playlist = await db.PlaylistRepository.InsertAsync(new Playlist
        {
            PlaylistId = "pl1",
            Name = "P",
            Url = "https://p",
            Platform = "YT",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var service = CreatePlaylistService(db);
        var result = await service.GetOrderSettingAsync(playlist.Id);
        Assert.True(result.IsSuccess);
        Assert.False(result.Value.UseCustomOrder);
    }

    [Fact]
    public async Task GetPlaylistVideosAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreatePlaylistService(db, userContext: CreateUserContext(null, false));
        var result = await service.GetPlaylistVideosAsync(1);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task GetPlaylistVideosAsync_WhenUserAndPlaylistExists_ReturnsSuccess()
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
        var playlist = await db.PlaylistRepository.InsertAsync(new Playlist
        {
            PlaylistId = "pl1",
            Name = "P",
            Url = "https://p",
            Platform = "YT",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var service = CreatePlaylistService(db);
        var result = await service.GetPlaylistVideosAsync(playlist.Id);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value.Videos);
    }

    [Fact]
    public async Task RefreshPlaylistsAsync_WhenNoAccess_ReturnsForbidden()
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
        var userContext = CreateUserContext("user1", false);
        var service = CreatePlaylistService(db, userContext: userContext);
        var result = await service.RefreshPlaylistsAsync(channel.Id);
        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task RefreshPlaylistsAsync_WhenChannelNotFound_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var userContext = CreateUserContext("user1", true);
        var service = CreatePlaylistService(db, userContext: userContext);
        var result = await service.RefreshPlaylistsAsync(999);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task SaveCustomOrderAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreatePlaylistService(db, userContext: CreateUserContext(null, false));
        var result = await service.SaveCustomOrderAsync(1, new ReorderVideosRequest { UseCustomOrder = false });
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task SaveCustomOrderAsync_WhenUser_ReturnsSuccess()
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
        var playlist = await db.PlaylistRepository.InsertAsync(new Playlist
        {
            PlaylistId = "pl1",
            Name = "P",
            Url = "https://p",
            Platform = "YT",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        await db.UserPlaylistRepository.InsertAsync(new UserPlaylist
        {
            UserId = "user1",
            PlaylistId = playlist.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var service = CreatePlaylistService(db);
        var result = await service.SaveCustomOrderAsync(playlist.Id, new ReorderVideosRequest { UseCustomOrder = false });
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SetVideoHiddenAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreatePlaylistService(db, userContext: CreateUserContext(null, false));
        var result = await service.SetVideoHiddenAsync(1, 1, new SetVideoHiddenRequest { IsHidden = true });
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task SetVideoHiddenAsync_WhenUser_ReturnsSuccess()
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
        var video = await db.VideoRepository.InsertAsync(new Video
        {
            VideoId = "v1",
            Title = "V",
            Url = "https://v",
            Platform = "YT",
            ChannelId = channel.Id,
            IsIgnored = false,
            IsQueued = false,
            NeedsMetadataReview = false
        });
        var playlist = await db.PlaylistRepository.InsertAsync(new Playlist
        {
            PlaylistId = "pl1",
            Name = "P",
            Url = "https://p",
            Platform = "YT",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        await db.PlaylistVideoRepository.InsertAsync(new PlaylistVideo { PlaylistId = playlist.Id, VideoId = video.Id, Order = 0 });
        var service = CreatePlaylistService(db);
        var result = await service.SetVideoHiddenAsync(playlist.Id, video.Id, new SetVideoHiddenRequest { IsHidden = true });
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SubscribeAllPlaylistsAsync_WhenNoAccess_ReturnsForbidden()
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
        var userContext = CreateUserContext("user1", false);
        var service = CreatePlaylistService(db, userContext: userContext);
        var result = await service.SubscribeAllPlaylistsAsync(channel.Id);
        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task SubscribeAllPlaylistsAsync_WhenAccessAndNoPlaylists_ReturnsSuccessWithMessage()
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
        var userContext = CreateUserContext("user1", false);
        var service = CreatePlaylistService(db, userContext: userContext);
        var result = await service.SubscribeAllPlaylistsAsync(channel.Id);
        Assert.True(result.IsSuccess);
        Assert.Equal("No playlists available to subscribe", result.Value.Message);
    }

    [Fact]
    public async Task SubscribePlaylistsAsync_WhenNoAccess_ReturnsForbidden()
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
        var userContext = CreateUserContext("user1", false);
        var service = CreatePlaylistService(db, userContext: userContext);
        var result = await service.SubscribePlaylistsAsync(channel.Id, new SubscribePlaylistsRequest { PlaylistIds = [1] });
        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task SubscribePlaylistsAsync_WhenPlaylistIdsEmpty_ReturnsInvalid()
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
        var service = CreatePlaylistService(db);
        var result = await service.SubscribePlaylistsAsync(channel.Id, new SubscribePlaylistsRequest { PlaylistIds = [] });
        Assert.Equal(ResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task SubscribePlaylistsAsync_WhenChannelNotFound_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var userContext = CreateUserContext("user1", true);
        var service = CreatePlaylistService(db, userContext: userContext);
        var result = await service.SubscribePlaylistsAsync(999, new SubscribePlaylistsRequest { PlaylistIds = [1] });
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public void SyncAllPlaylists_ReturnsSuccess()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreatePlaylistService(db);
        var result = service.SyncAllPlaylists();
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ToggleIgnoreAsync_WhenNoAccess_ReturnsForbidden()
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
        var playlist = await db.PlaylistRepository.InsertAsync(new Playlist
        {
            PlaylistId = "pl1",
            Name = "P",
            Url = "https://p",
            Platform = "YT",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var userContext = CreateUserContext("user1", false);
        var service = CreatePlaylistService(db, userContext: userContext);
        var result = await service.ToggleIgnoreAsync(channel.Id, playlist.Id, new IgnorePlaylistRequest { IsIgnored = true });
        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task ToggleIgnoreAsync_WhenAdmin_ReturnsSuccess()
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
        var playlist = await db.PlaylistRepository.InsertAsync(new Playlist
        {
            PlaylistId = "pl1",
            Name = "P",
            Url = "https://p",
            Platform = "YT",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow,
            IsIgnored = false
        });
        var userContext = CreateUserContext("user1", true);
        var service = CreatePlaylistService(db, userContext: userContext);
        var result = await service.ToggleIgnoreAsync(channel.Id, playlist.Id, new IgnorePlaylistRequest { IsIgnored = true });
        Assert.True(result.IsSuccess);
    }
}