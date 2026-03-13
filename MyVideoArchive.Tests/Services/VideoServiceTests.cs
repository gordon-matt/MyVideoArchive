using Hangfire;
using MyVideoArchive.Models.Video;

namespace MyVideoArchive.Tests.Services;

public class VideoServiceTests
{
    private static VideoService CreateVideoService(
        InMemoryDatabaseFixture db,
        IUserContextService? userContext = null,
        IChannelService? channelService = null,
        ICustomPlaylistService? customPlaylistService = null,
        ITagService? tagService = null,
        VideoMetadataProviderFactory? metadataFactory = null)
    {
        var user = userContext ?? CreateUserContext("user1", false);
        var channelSvc = channelService ?? Mock.Of<IChannelService>();
        var playlistSvc = customPlaylistService ?? Mock.Of<ICustomPlaylistService>();

        var tagSvc = tagService ?? new TagService(
            NullLogger<TagService>.Instance,
            user,
            db.TagRepository,
            db.VideoRepository,
            db.VideoTagRepository,
            db.ChannelTagRepository,
            db.PlaylistTagRepository,
            db.CustomPlaylistTagRepository,
            db.CustomPlaylistRepository);

        var factory = metadataFactory ?? new VideoMetadataProviderFactory(
            [],
            NullLogger<VideoMetadataProviderFactory>.Instance);
        return new VideoService(
            NullLogger<VideoService>.Instance,
            user,
            Mock.Of<IBackgroundJobClient>(),
            factory,
            channelSvc,
            playlistSvc,
            tagSvc,
            db.ChannelRepository,
            db.PlaylistVideoRepository,
            db.UserChannelRepository,
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

    [Fact]
    public async Task AddStandaloneVideoAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateVideoService(db, userContext: CreateUserContext(null, false));
        var result = await service.AddStandaloneVideoAsync(new AddStandaloneVideoRequest { Url = "https://example.com/v" });
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task AddStandaloneVideoAsync_WhenUrlEmpty_ReturnsInvalid()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateVideoService(db);
        var result = await service.AddStandaloneVideoAsync(new AddStandaloneVideoRequest { Url = "   " });
        Assert.Equal(ResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task AddStandaloneVideoAsync_WhenNoProviderForUrl_ReturnsInvalid()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateVideoService(db);
        var result = await service.AddStandaloneVideoAsync(new AddStandaloneVideoRequest { Url = "https://unknown.com/v/1" });
        Assert.Equal(ResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task DeleteVideoFileAsync_WhenVideoNotFound_ReturnsNotFound()
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
        var service = CreateVideoService(db);
        var result = await service.DeleteVideoFileAsync(channel.Id, 999);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task DeleteVideoFileAsync_WhenVideoExists_ReturnsSuccess()
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
        var customPlaylist = new Mock<ICustomPlaylistService>();
        customPlaylist.Setup(x => x.VideoAppearsOnAnyPlaylistsForOtherUsers(video.Id, "user1")).ReturnsAsync(false);
        customPlaylist.Setup(x => x.RemoveVideoFromAllPlaylistsForUserAsync(video.Id, "user1")).ReturnsAsync(Result.Success());
        var service = CreateVideoService(db, userContext: CreateUserContext("user1", false), customPlaylistService: customPlaylist.Object);
        var result = await service.DeleteVideoFileAsync(channel.Id, video.Id);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetAccessibleChannelsAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateVideoService(db, userContext: CreateUserContext(null, false));
        var result = await service.GetAccessibleChannelsAsync();
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task GetAccessibleChannelsAsync_WhenUser_ReturnsSuccess()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateVideoService(db);
        var result = await service.GetAccessibleChannelsAsync();
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task GetFailedDownloadsAsync_ReturnsSuccess()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateVideoService(db);
        var result = await service.GetFailedDownloadsAsync();
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value.Videos);
    }

    [Fact]
    public async Task GetStandaloneInfoAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateVideoService(db, userContext: CreateUserContext(null, false));
        var result = await service.GetStandaloneInfoAsync(1);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task GetStandaloneInfoAsync_WhenVideoNotFound_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateVideoService(db);
        var result = await service.GetStandaloneInfoAsync(999);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task GetStandaloneInfoAsync_WhenVideoExists_ReturnsSuccess()
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
        var service = CreateVideoService(db);
        var result = await service.GetStandaloneInfoAsync(video.Id);
        Assert.True(result.IsSuccess);
        Assert.Equal(channel.Name, result.Value.ChannelName);
    }

    [Fact]
    public async Task GetVideoPlaylistsAsync_ReturnsSuccess()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateVideoService(db);
        var result = await service.GetVideoPlaylistsAsync(1);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value.Playlists);
    }

    [Fact]
    public async Task GetVideosAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateVideoService(db, userContext: CreateUserContext(null, false));
        var result = await service.GetVideosAsync();
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task GetVideosAsync_WhenUser_ReturnsSuccess()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateVideoService(db);
        var result = await service.GetVideosAsync();
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task GetVideoStreamInfoAsync_WhenVideoNotFound_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateVideoService(db);
        var result = await service.GetVideoStreamInfoAsync(999);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task GetVideoStreamInfoAsync_WhenVideoExistsWithoutFile_ReturnsNotFound()
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
            NeedsMetadataReview = false,
            FilePath = null
        });
        var service = CreateVideoService(db);
        var result = await service.GetVideoStreamInfoAsync(video.Id);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task GetWatchedVideoIdsAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateVideoService(db, userContext: CreateUserContext(null, false));
        var result = await service.GetWatchedVideoIdsAsync([1, 2]);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task GetWatchedVideoIdsAsync_WhenUser_ReturnsSuccess()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateVideoService(db);
        var result = await service.GetWatchedVideoIdsAsync([1, 2]);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value.WatchedIds);
    }

    [Fact]
    public async Task GetWatchedVideoIdsByChannelAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateVideoService(db, userContext: CreateUserContext(null, false));
        var result = await service.GetWatchedVideoIdsByChannelAsync(1);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task GetWatchedVideoIdsByPlaylistAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateVideoService(db, userContext: CreateUserContext(null, false));
        var result = await service.GetWatchedVideoIdsByPlaylistAsync(1);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task MarkUnwatchedAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateVideoService(db, userContext: CreateUserContext(null, false));
        var result = await service.MarkUnwatchedAsync(1);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task MarkUnwatchedAsync_WhenUser_CompletesWithoutThrow()
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
        var service = CreateVideoService(db);
        var result = await service.MarkUnwatchedAsync(video.Id);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task MarkWatchedAsync_WhenNoUser_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateVideoService(db, userContext: CreateUserContext(null, false));
        var result = await service.MarkWatchedAsync(1);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task MarkWatchedAsync_WhenUser_ReturnsSuccess()
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
        var service = CreateVideoService(db);
        var result = await service.MarkWatchedAsync(video.Id);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ToggleIgnoreAsync_WhenNoUser_ReturnsForbidden()
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
        var service = CreateVideoService(db, userContext: CreateUserContext(null, false));
        var result = await service.ToggleIgnoreAsync(channel.Id, 1, new IgnoreVideoRequest { IsIgnored = true });
        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task ToggleIgnoreAsync_WhenVideoNotFound_ReturnsNotFoundOrForbidden()
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
        var service = CreateVideoService(db);
        var result = await service.ToggleIgnoreAsync(channel.Id, 999, new IgnoreVideoRequest { IsIgnored = true });
        Assert.True(result.Status is ResultStatus.NotFound or ResultStatus.Forbidden);
    }

    [Fact]
    public async Task ToggleIgnoreAsync_WhenSubscribedAndVideoExists_CompletesWithAccess()
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
        var channelService = new Mock<IChannelService>();
        channelService.Setup(x => x.UserSubscribedToChannelAsync(channel.Id)).ReturnsAsync(true);
        var service = CreateVideoService(db, channelService: channelService.Object);
        var result = await service.ToggleIgnoreAsync(channel.Id, video.Id, new IgnoreVideoRequest { IsIgnored = true });
        Assert.True(result.Status is not ResultStatus.Forbidden and not ResultStatus.NotFound);
    }

    [Fact]
    public async Task RetryMetadataAsync_WhenVideoNotFound_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateVideoService(db);
        var result = await service.RetryMetadataAsync(999);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task RetryMetadataAsync_WhenNoProviderForPlatform_ReturnsInvalid()
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
            NeedsMetadataReview = true
        });
        var service = CreateVideoService(db);
        var result = await service.RetryMetadataAsync(video.Id);
        Assert.Equal(ResultStatus.Invalid, result.Status);
    }
}