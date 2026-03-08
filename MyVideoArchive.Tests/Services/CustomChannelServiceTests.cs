using MyVideoArchive.Models.Requests.Channel;

namespace MyVideoArchive.Tests.Services;

public class CustomChannelServiceTests
{
    private static CustomChannelService CreateService(
        InMemoryDatabaseFixture db,
        IUserContextService? userContext = null)
    {
        var user = userContext ?? CreateUserContext("user1");
        return new CustomChannelService(
            NullLogger<CustomChannelService>.Instance,
            new ConfigurationBuilder().Build(),
            user,
            db.ChannelRepository,
            db.PlaylistRepository,
            db.PlaylistVideoRepository,
            db.UserChannelRepository,
            db.VideoRepository);
    }

    private static IUserContextService CreateUserContext(string? userId)
    {
        var mock = new Mock<IUserContextService>();
        mock.Setup(c => c.GetCurrentUserId()).Returns(userId);
        return mock.Object;
    }

    [Fact]
    public async Task CreateChannelAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db, CreateUserContext(null));
        var result = await service.CreateChannelAsync(new CreateCustomChannelRequest("My Channel", null));
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task CreateChannelAsync_WhenUser_ReturnsSuccess()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db);
        var result = await service.CreateChannelAsync(new CreateCustomChannelRequest("My Channel", null));
        Assert.True(result.IsSuccess);
        Assert.Equal("My Channel", result.Value.Name);
        Assert.Equal("Custom", result.Value.Platform);
    }

    [Fact]
    public async Task CreatePlaylistAsync_WhenNoAccess_ReturnsForbidden()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "c1",
            Name = "C",
            Url = "https://c",
            Platform = "Custom",
            SubscribedAt = DateTime.UtcNow
        });
        var otherUser = CreateUserContext("other");
        var service = CreateService(db, otherUser);
        var result = await service.CreatePlaylistAsync(channel.Id, new CreateCustomChannelPlaylistRequest("P", null));
        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task CreatePlaylistAsync_WhenChannelNotFound_ReturnsForbidden()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db);
        var result = await service.CreatePlaylistAsync(999, new CreateCustomChannelPlaylistRequest("P", null));
        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task CreatePlaylistAsync_WhenChannelExistsAndUserSubscribed_ReturnsSuccess()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "c1",
            Name = "C",
            Url = "https://c",
            Platform = "Custom",
            SubscribedAt = DateTime.UtcNow
        });
        await db.UserChannelRepository.InsertAsync(new UserChannel
        {
            UserId = "user1",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var service = CreateService(db);
        var result = await service.CreatePlaylistAsync(channel.Id, new CreateCustomChannelPlaylistRequest("My Playlist", null));
        Assert.True(result.IsSuccess);
        Assert.Equal("My Playlist", result.Value.Name);
    }

    [Fact]
    public async Task DeletePlaylistAsync_WhenPlaylistNotFound_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db);
        var result = await service.DeletePlaylistAsync(999);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task GetChannelPlaylistsAsync_WhenNoAccess_ReturnsForbidden()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "c1",
            Name = "C",
            Url = "https://c",
            Platform = "Custom",
            SubscribedAt = DateTime.UtcNow
        });
        var otherUser = CreateUserContext("other");
        var service = CreateService(db, otherUser);
        var result = await service.GetChannelPlaylistsAsync(channel.Id);
        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task GetChannelPlaylistsAsync_WhenChannelNotFound_ReturnsForbidden()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db);
        var result = await service.GetChannelPlaylistsAsync(999);
        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task GetChannelPlaylistsAsync_WhenAccess_ReturnsSuccess()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "c1",
            Name = "C",
            Url = "https://c",
            Platform = "Custom",
            SubscribedAt = DateTime.UtcNow
        });
        await db.UserChannelRepository.InsertAsync(new UserChannel
        {
            UserId = "user1",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var service = CreateService(db);
        var result = await service.GetChannelPlaylistsAsync(channel.Id);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value.Playlists);
    }

    [Fact]
    public async Task GetChannelThumbnailAsync_WhenNoAccess_ReturnsForbidden()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "c1",
            Name = "C",
            Url = "https://c",
            Platform = "Custom",
            SubscribedAt = DateTime.UtcNow
        });
        var otherUser = CreateUserContext("other");
        var service = CreateService(db, otherUser);
        var result = await service.GetChannelThumbnailAsync(channel.Id);
        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task GetChannelThumbnailAsync_WhenSubscribed_Completes()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "c1",
            Name = "C",
            Url = "https://c",
            Platform = "Custom",
            SubscribedAt = DateTime.UtcNow
        });
        await db.UserChannelRepository.InsertAsync(new UserChannel
        {
            UserId = "user1",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var service = CreateService(db);
        var result = await service.GetChannelThumbnailAsync(channel.Id);
        Assert.True(result.Status is ResultStatus.NotFound or ResultStatus.Ok);
    }

    [Fact]
    public async Task GetPlaylistThumbnailAsync_WhenPlaylistNotFound_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db);
        var result = await service.GetPlaylistThumbnailAsync(999);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task GetVideoPlaylistIdsAsync_WhenVideoNotFound_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db);
        var result = await service.GetVideoPlaylistIdsAsync(999);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task GetVideoThumbnailAsync_WhenVideoNotFound_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db);
        var result = await service.GetVideoThumbnailAsync(999);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task UpdateChannelAsync_WhenNoAccess_ReturnsForbidden()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "c1",
            Name = "C",
            Url = "https://c",
            Platform = "Custom",
            SubscribedAt = DateTime.UtcNow
        });
        var otherUser = CreateUserContext("other");
        var service = CreateService(db, otherUser);
        var result = await service.UpdateChannelAsync(channel.Id, new UpdateCustomChannelRequest("New", null, null));
        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task UpdateChannelAsync_WhenAccess_ReturnsSuccess()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "c1",
            Name = "C",
            Url = "https://c",
            Platform = "Custom",
            SubscribedAt = DateTime.UtcNow
        });
        await db.UserChannelRepository.InsertAsync(new UserChannel
        {
            UserId = "user1",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var service = CreateService(db);
        var result = await service.UpdateChannelAsync(channel.Id, new UpdateCustomChannelRequest("Updated", null, null));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task UpdatePlaylistAsync_WhenPlaylistNotFound_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db);
        var result = await service.UpdatePlaylistAsync(999, new UpdateCustomChannelPlaylistRequest("P", null));
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task UpdateVideoAsync_WhenVideoNotFound_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db);
        var result = await service.UpdateVideoAsync(999, new UpdateCustomVideoRequest("T", null, null, null, null, null, null));
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task UploadChannelThumbnailAsync_WhenSubscribed_Completes()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "c1",
            Name = "C",
            Url = "https://c",
            Platform = "Custom",
            SubscribedAt = DateTime.UtcNow
        });
        await db.UserChannelRepository.InsertAsync(new UserChannel
        {
            UserId = "user1",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var service = CreateService(db);
        await using var stream = new MemoryStream();
        var result = await service.UploadChannelThumbnailAsync(channel.Id, stream, "file.jpg");
        Assert.True(result.IsSuccess || result.Status == ResultStatus.Invalid);
    }

    [Fact]
    public async Task UploadPlaylistThumbnailAsync_WhenSubscribed_Completes()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "c1",
            Name = "C",
            Url = "https://c",
            Platform = "Custom",
            SubscribedAt = DateTime.UtcNow
        });
        var playlist = await db.PlaylistRepository.InsertAsync(new Playlist
        {
            PlaylistId = "p1",
            Name = "P",
            Url = "https://p",
            Platform = "Custom",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        await db.UserChannelRepository.InsertAsync(new UserChannel
        {
            UserId = "user1",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var service = CreateService(db);
        await using var stream = new MemoryStream();
        var result = await service.UploadPlaylistThumbnailAsync(playlist.Id, stream, "file.jpg");
        Assert.True(result.IsSuccess || result.Status == ResultStatus.Invalid);
    }

    [Fact]
    public async Task UploadVideoThumbnailAsync_WhenInvalidExtension_ReturnsInvalid()
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
        await db.UserChannelRepository.InsertAsync(new UserChannel
        {
            UserId = "user1",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var service = CreateService(db);
        await using var stream = new MemoryStream();
        var result = await service.UploadVideoThumbnailAsync(video.Id, stream, "file.txt");
        Assert.Equal(ResultStatus.Invalid, result.Status);
    }
}