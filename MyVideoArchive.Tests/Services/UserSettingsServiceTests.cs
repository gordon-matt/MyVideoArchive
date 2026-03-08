namespace MyVideoArchive.Tests.Services;

public class UserSettingsServiceTests
{
    [Fact]
    public async Task GetSettingsAsync_WhenUserIsNull_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var userContext = new Mock<IUserContextService>();
        userContext.Setup(c => c.GetCurrentUserId()).Returns((string?)null);
        var service = new UserSettingsService(
            NullLogger<UserSettingsService>.Instance,
            userContext.Object,
            db.UserSettingsRepository);

        var result = await service.GetSettingsAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task GetSettingsAsync_WhenUserHasNoEntry_ReturnsDefaults()
    {
        using var db = new InMemoryDatabaseFixture();
        var userContext = new Mock<IUserContextService>();
        userContext.Setup(c => c.GetCurrentUserId()).Returns("user-1");
        var service = new UserSettingsService(
            NullLogger<UserSettingsService>.Instance,
            userContext.Object,
            db.UserSettingsRepository);

        var result = await service.GetSettingsAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("list", result.Value.VideosTabViewMode);
        Assert.Equal("list", result.Value.AvailableTabViewMode);
    }

    [Fact]
    public async Task GetSettingsAsync_WhenUserHasEntry_ReturnsStoredValues()
    {
        using var db = new InMemoryDatabaseFixture();
        using var ctx = db.CreateContext();
        var user = new Data.Entities.ApplicationUser { UserName = "u", Email = "u@u.com" };
        ctx.Set<Data.Entities.ApplicationUser>().Add(user);
        await ctx.SaveChangesAsync();
        await db.UserSettingsRepository.InsertAsync(new Data.Entities.UserSettingsEntry
        {
            UserId = user.Id,
            VideosTabViewMode = Data.Enums.ViewMode.Grid,
            AvailableTabViewMode = Data.Enums.ViewMode.List
        });
        var userContext = new Mock<IUserContextService>();
        userContext.Setup(c => c.GetCurrentUserId()).Returns(user.Id);
        var service = new UserSettingsService(
            NullLogger<UserSettingsService>.Instance,
            userContext.Object,
            db.UserSettingsRepository);

        var result = await service.GetSettingsAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("grid", result.Value.VideosTabViewMode);
        Assert.Equal("list", result.Value.AvailableTabViewMode);
    }

    [Fact]
    public async Task UpdateSettingsAsync_WhenUserIsNull_ReturnsUnauthorized()
    {
        using var db = new InMemoryDatabaseFixture();
        var userContext = new Mock<IUserContextService>();
        userContext.Setup(c => c.GetCurrentUserId()).Returns((string?)null);
        var service = new UserSettingsService(
            NullLogger<UserSettingsService>.Instance,
            userContext.Object,
            db.UserSettingsRepository);

        var result = await service.UpdateSettingsAsync(new UpdateUserSettingsRequest());

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task UpdateSettingsAsync_WhenUserSet_UpdatesOrCreatesEntry()
    {
        using var db = new InMemoryDatabaseFixture();
        using var ctx = db.CreateContext();
        var user = new Data.Entities.ApplicationUser { UserName = "u", Email = "u@u.com" };
        ctx.Set<Data.Entities.ApplicationUser>().Add(user);
        await ctx.SaveChangesAsync();
        var userContext = new Mock<IUserContextService>();
        userContext.Setup(c => c.GetCurrentUserId()).Returns(user.Id);
        var service = new UserSettingsService(
            NullLogger<UserSettingsService>.Instance,
            userContext.Object,
            db.UserSettingsRepository);

        var result = await service.UpdateSettingsAsync(new UpdateUserSettingsRequest
        {
            VideosTabViewMode = "grid",
            AvailableTabViewMode = "list"
        });

        Assert.True(result.IsSuccess);
        var entry = await db.UserSettingsRepository.FindOneAsync(new SearchOptions<UserSettingsEntry>
        { Query = x => x.UserId == user.Id });
        Assert.NotNull(entry);
        Assert.Equal(MyVideoArchive.Data.Enums.ViewMode.Grid, entry.VideosTabViewMode);
    }
}