using Hangfire;
using MyVideoArchive.Models.Requests.Playlist;

namespace MyVideoArchive.Tests.Services;

public class CustomPlaylistServiceTests
{
    private static CustomPlaylistService CreateService(
        InMemoryDatabaseFixture db,
        IUserContextService? userContext = null)
    {
        var user = userContext ?? CreateUserContext("user1");
        var factory = new VideoMetadataProviderFactory(
            [],
            NullLogger<VideoMetadataProviderFactory>.Instance);
        return new CustomPlaylistService(
            NullLogger<CustomPlaylistService>.Instance,
            new ConfigurationBuilder().Build(),
            user,
            Mock.Of<IBackgroundJobClient>(),
            factory,
            Mock.Of<IHttpClientFactory>(),
            db.ChannelRepository,
            db.CustomPlaylistRepository,
            db.CustomPlaylistVideoRepository,
            db.TagRepository,
            db.UserChannelRepository,
            db.VideoRepository,
            db.VideoTagRepository);
    }

    private static IUserContextService CreateUserContext(string? userId)
    {
        var mock = new Mock<IUserContextService>();
        mock.Setup(c => c.GetCurrentUserId()).Returns(userId);
        return mock.Object;
    }

    [Fact]
    public async Task AddVideoToPlaylistAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db, CreateUserContext(null));
        var result = await service.AddVideoToPlaylistAsync(1, 1);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task AddVideoToPlaylistAsync_WhenPlaylistNotFound_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db);
        var result = await service.AddVideoToPlaylistAsync(999, 1);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task AddVideoToPlaylistAsync_WhenVideoNotFound_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var playlist = await db.CustomPlaylistRepository.InsertAsync(new CustomPlaylist
        {
            UserId = "user1",
            Name = "P",
            Description = null,
            CreatedAt = DateTime.UtcNow
        });
        var service = CreateService(db);
        var result = await service.AddVideoToPlaylistAsync(playlist.Id, 999);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task AddVideoToPlaylistAsync_WhenNotOwner_ReturnsForbidden()
    {
        using var db = new InMemoryDatabaseFixture();
        var playlist = await db.CustomPlaylistRepository.InsertAsync(new CustomPlaylist
        {
            UserId = "other",
            Name = "P",
            Description = null,
            CreatedAt = DateTime.UtcNow
        });
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
        var service = CreateService(db);
        var result = await service.AddVideoToPlaylistAsync(playlist.Id, video.Id);
        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task AddVideoToPlaylistAsync_WhenOwnerAndVideoExists_ReturnsSuccess()
    {
        using var db = new InMemoryDatabaseFixture();
        var playlist = await db.CustomPlaylistRepository.InsertAsync(new CustomPlaylist
        {
            UserId = "user1",
            Name = "P",
            Description = null,
            CreatedAt = DateTime.UtcNow
        });
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
        var service = CreateService(db);
        var result = await service.AddVideoToPlaylistAsync(playlist.Id, video.Id);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeletePlaylistAsync_WhenOwner_ReturnsSuccess()
    {
        using var db = new InMemoryDatabaseFixture();
        var playlist = await db.CustomPlaylistRepository.InsertAsync(new CustomPlaylist
        {
            UserId = "user1",
            Name = "P",
            Description = null,
            CreatedAt = DateTime.UtcNow
        });
        var service = CreateService(db);
        var result = await service.DeletePlaylistAsync(playlist.Id);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ClonePlaylistAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db, CreateUserContext(null));
        var result = await service.ClonePlaylistAsync(new ClonePlaylistRequest { Url = "https://x.com", SelectedVideoIds = ["v1"] });
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task ClonePlaylistAsync_WhenUrlEmpty_ReturnsInvalid()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db);
        var result = await service.ClonePlaylistAsync(new ClonePlaylistRequest { Url = "  ", SelectedVideoIds = ["v1"] });
        Assert.Equal(ResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task CreatePlaylistAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db, CreateUserContext(null));
        var result = await service.CreatePlaylistAsync(new CreateCustomPlaylistRequest { Name = "P" });
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task CreatePlaylistAsync_WhenUser_ReturnsSuccess()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db);
        var result = await service.CreatePlaylistAsync(new CreateCustomPlaylistRequest { Name = "My List" });
        Assert.True(result.IsSuccess);
        Assert.Equal("My List", result.Value.Name);
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
    public async Task GetPlaylistsAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db, CreateUserContext(null));
        var result = await service.GetPlaylistsAsync();
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task GetPlaylistsAsync_WhenUser_ReturnsSuccess()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db);
        var result = await service.GetPlaylistsAsync();
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task GetPlaylistsForVideoAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db, CreateUserContext(null));
        var result = await service.GetPlaylistsForVideoAsync(1);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task GetPlaylistsForVideoAsync_WhenUser_ReturnsSuccess()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db);
        var result = await service.GetPlaylistsForVideoAsync(1);
        Assert.True(result.IsSuccess);
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
    public async Task GetPlaylistVideosAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db, CreateUserContext(null));
        var result = await service.GetPlaylistVideosAsync(1);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task GetPlaylistVideosAsync_WhenPlaylistNotFound_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db);
        var result = await service.GetPlaylistVideosAsync(999);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task PreviewPlaylistAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db, CreateUserContext(null));
        var result = await service.PreviewPlaylistAsync(new PreviewPlaylistRequest { Url = "https://x.com" });
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task RemoveVideoFromPlaylistAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db, CreateUserContext(null));
        var result = await service.RemoveVideoFromPlaylistAsync(1, 1);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task RemoveVideoFromAllPlaylistsAsync_WhenNoUser_ReturnsUnauthorizedOrError()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db, CreateUserContext(null));
        var result = await service.RemoveVideoFromAllPlaylistsAsync(1);
        Assert.True(result.Status is ResultStatus.Unauthorized or ResultStatus.Error);
    }

    [Fact]
    public async Task RemoveVideoFromAllPlaylistsForUserAsync_CompletesWithoutThrow()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db);
        var result = await service.RemoveVideoFromAllPlaylistsForUserAsync(1, "user1");
        Assert.NotNull(result);
    }

    [Fact]
    public async Task UpdatePlaylistAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db, CreateUserContext(null));
        var result = await service.UpdatePlaylistAsync(1, new CreateCustomPlaylistRequest { Name = "P" });
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task UpdatePlaylistAsync_WhenPlaylistNotFound_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db);
        var result = await service.UpdatePlaylistAsync(999, new CreateCustomPlaylistRequest { Name = "P" });
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task VideoAppearsOnAnyPlaylistsForOtherUsers_ReturnsFalseWhenNone()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateService(db);
        var result = await service.VideoAppearsOnAnyPlaylistsForOtherUsers(1, "user1");
        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }
}