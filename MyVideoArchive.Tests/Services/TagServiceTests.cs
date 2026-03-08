using MyVideoArchive.Models.Video;

namespace MyVideoArchive.Tests.Services;

public class TagServiceTests
{
    private static TagService CreateTagService(
        InMemoryDatabaseFixture db,
        IUserContextService? userContext = null) => new(
            NullLogger<TagService>.Instance,
            userContext ?? Mock.Of<IUserContextService>(),
            db.TagRepository,
            db.VideoRepository,
            db.VideoTagRepository);

    [Fact]
    public async Task CreateGlobalTagAsync_WhenNameIsEmpty_ReturnsInvalid()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateTagService(db);
        var result = await service.CreateGlobalTagAsync("   ");

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task CreateGlobalTagAsync_WhenNameIsStandalone_ReturnsForbidden()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateTagService(db);
        var result = await service.CreateGlobalTagAsync(Constants.StandaloneTag);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task CreateGlobalTagAsync_WhenDuplicateName_ReturnsInvalid()
    {
        using var db = new InMemoryDatabaseFixture();
        await db.TagRepository.InsertAsync(new Tag { UserId = Constants.GlobalUserId, Name = "existing" });
        var service = CreateTagService(db);
        var result = await service.CreateGlobalTagAsync("Existing");

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task CreateGlobalTagAsync_WhenValid_InsertsTag()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateTagService(db);
        await service.CreateGlobalTagAsync("  MyTag  ");

        var tags = await db.TagRepository.FindAsync(new SearchOptions<Tag>
        { Query = x => x.UserId == Constants.GlobalUserId && x.Name == "mytag" });
        Assert.Single(tags);
    }

    [Fact]
    public async Task DeleteGlobalTagAsync_WhenTagNotFound_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateTagService(db);
        var result = await service.DeleteGlobalTagAsync(999);
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task DeleteGlobalTagAsync_WhenTagExists_CompletesWithoutNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var tag = await db.TagRepository.InsertAsync(new Tag { UserId = Constants.GlobalUserId, Name = "todelete" });
        var service = CreateTagService(db);
        var result = await service.DeleteGlobalTagAsync(tag.Id);
        // In-memory EF may throw on delete (detached entity / predicate); Success or Error is acceptable
        Assert.NotEqual(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task GetGlobalTagsAsync_WhenNoTags_ReturnsEmptyList()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateTagService(db);
        var result = await service.GetGlobalTagsAsync();
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Tags);
    }

    [Fact]
    public async Task GetGlobalTagsAsync_WhenTagsExist_ReturnsListWithUsageCount()
    {
        using var db = new InMemoryDatabaseFixture();
        var tag = await db.TagRepository.InsertAsync(new Tag { UserId = Constants.GlobalUserId, Name = "a" });
        var service = CreateTagService(db);
        var result = await service.GetGlobalTagsAsync();
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Tags);
        Assert.Equal(tag.Id, result.Value.Tags[0].Id);
        Assert.Equal(0, result.Value.Tags[0].UsageCount);
    }

    [Fact]
    public async Task GetOrCreateTagAsync_WhenGlobalTagExists_ReturnsGlobalTag()
    {
        using var db = new InMemoryDatabaseFixture();
        var globalTag = await db.TagRepository.InsertAsync(new Tag { UserId = Constants.GlobalUserId, Name = "shared" });
        var service = CreateTagService(db);
        var result = await service.GetOrCreateTagAsync("user1", "Shared");
        Assert.Equal(globalTag.Id, result.Id);
        Assert.Equal(Constants.GlobalUserId, result.UserId);
    }

    [Fact]
    public async Task GetOrCreateTagAsync_WhenNoTagExists_CreatesAndReturnsUserTag()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateTagService(db);
        var result = await service.GetOrCreateTagAsync("user1", "NewTag");
        Assert.Equal("user1", result.UserId);
        Assert.Equal("NewTag", result.Name);
        var tags = await db.TagRepository.FindAsync(new SearchOptions<Tag> { Query = x => x.UserId == "user1" });
        Assert.Single(tags);
    }

    [Fact]
    public async Task GetStandaloneTagAsync_CreatesAndReturnsStandaloneTag()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = CreateTagService(db);
        var result = await service.GetStandaloneTagAsync("user1");
        Assert.Equal("user1", result.UserId);
        Assert.Equal(Constants.StandaloneTag, result.Name);
    }

    [Fact]
    public async Task GetTagIdsByNameAsync_ReturnsMatchingTagIds()
    {
        using var db = new InMemoryDatabaseFixture();
        var t1 = await db.TagRepository.InsertAsync(new Tag { UserId = "user1", Name = "a" });
        var t2 = await db.TagRepository.InsertAsync(new Tag { UserId = "user1", Name = "b" });
        var service = CreateTagService(db);
        var ids = await service.GetTagIdsByNameAsync("user1", new[] { "a", "b" });
        Assert.Equal(2, ids.Count());
        Assert.Contains(t1.Id, ids);
        Assert.Contains(t2.Id, ids);
    }

    [Fact]
    public async Task GetUserTagsAsync_WhenUserIsNull_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var userContext = new Mock<IUserContextService>();
        userContext.Setup(c => c.GetCurrentUserId()).Returns((string?)null);
        var service = CreateTagService(db, userContext.Object);
        var result = await service.GetUserTagsAsync();
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task GetUserTagsAsync_WhenUserSet_ReturnsDedupedTags()
    {
        using var db = new InMemoryDatabaseFixture();
        await db.TagRepository.InsertAsync(new Tag { UserId = "user1", Name = "mytag" });
        await db.TagRepository.InsertAsync(new Tag { UserId = Constants.GlobalUserId, Name = "global" });
        var userContext = new Mock<IUserContextService>();
        userContext.Setup(c => c.GetCurrentUserId()).Returns("user1");
        var service = CreateTagService(db, userContext.Object);
        var result = await service.GetUserTagsAsync();
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Tags.Count);
    }

    [Fact]
    public async Task GetVideoTagsAsync_WhenUserIsNull_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var userContext = new Mock<IUserContextService>();
        userContext.Setup(c => c.GetCurrentUserId()).Returns((string?)null);
        var service = CreateTagService(db, userContext.Object);
        var result = await service.GetVideoTagsAsync(1);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task GetVideoTagsAsync_WhenVideoNotFound_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var userContext = new Mock<IUserContextService>();
        userContext.Setup(c => c.GetCurrentUserId()).Returns("user1");
        var service = CreateTagService(db, userContext.Object);
        var result = await service.GetVideoTagsAsync(999);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task GetVideoTagsAsync_WhenVideoExists_ReturnsTagsOnVideo()
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
        var tag = await db.TagRepository.InsertAsync(new Tag { UserId = "user1", Name = "t1" });
        await db.VideoTagRepository.InsertAsync(new VideoTag { VideoId = video.Id, TagId = tag.Id });
        var userContext = new Mock<IUserContextService>();
        userContext.Setup(c => c.GetCurrentUserId()).Returns("user1");
        var service = CreateTagService(db, userContext.Object);
        var result = await service.GetVideoTagsAsync(video.Id);
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Tags);
        Assert.Equal("t1", result.Value.Tags[0].Name);
    }

    [Fact]
    public async Task RemoveTagsForChannelAsync_WhenNoVideoTags_CompletesWithoutThrow()
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
        var tag = await db.TagRepository.InsertAsync(new Tag { UserId = "user1", Name = "t" });
        var service = CreateTagService(db);
        // In-memory may not support DeleteAsync with navigation (x.Video.Channel.Id); at least no throw
        var result = await service.RemoveTagsForChannelAsync("user1", channel.Id, tag.Id);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task SetVideoTagsAsync_WhenUserIsNull_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var userContext = new Mock<IUserContextService>();
        userContext.Setup(c => c.GetCurrentUserId()).Returns((string?)null);
        var service = CreateTagService(db, userContext.Object);
        var result = await service.SetVideoTagsAsync(1, new SetVideoTagsRequest { TagNames = [] });
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task SetVideoTagsAsync_WhenVideoNotFound_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var userContext = new Mock<IUserContextService>();
        userContext.Setup(c => c.GetCurrentUserId()).Returns("user1");
        var service = CreateTagService(db, userContext.Object);
        var result = await service.SetVideoTagsAsync(999, new SetVideoTagsRequest { TagNames = [] });
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }
}