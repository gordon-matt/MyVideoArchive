using MyVideoArchive.Data;

namespace MyVideoArchive.Tests.Services;

public class AspNetIdentityUserInfoServiceTests
{
    private static ApplicationUser SeedUser(string id, string userName, string email) =>
        new()
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N")
        };

    [Fact]
    public async Task GetUserInfoAsync_WhenNoIds_ReturnsEmptyDictionary()
    {
        using var db = new InMemoryDatabaseFixture();
        var service = new AspNetIdentityUserInfoService(db.DbContextFactory);
        var map = await service.GetUserInfoAsync([]);
        Assert.Empty(map);
    }

    [Fact]
    public async Task GetUserInfoAsync_ReturnsMatchingUsersOnly()
    {
        using var db = new InMemoryDatabaseFixture();
        await using (var ctx = (ApplicationDbContextBase)db.DbContextFactory.GetContext())
        {
            ctx.Users.AddRange(
                SeedUser("u1", "alice", "a@a.com"),
                SeedUser("u2", "bob", "b@b.com"));
            await ctx.SaveChangesAsync();
        }

        var service = new AspNetIdentityUserInfoService(db.DbContextFactory);
        var map = await service.GetUserInfoAsync(["u1", "missing"]);

        Assert.Single(map);
        Assert.Equal("alice", map["u1"].Username);
        Assert.Equal("a@a.com", map["u1"].Email);
    }

    [Fact]
    public async Task GetAllUsersAsync_ReturnsUsersOrderedByUserName()
    {
        using var db = new InMemoryDatabaseFixture();
        await using (var ctx = (ApplicationDbContextBase)db.DbContextFactory.GetContext())
        {
            ctx.Users.AddRange(
                SeedUser("u1", "charlie", "c@c.com"),
                SeedUser("u2", "bravo", "b@b.com"));
            await ctx.SaveChangesAsync();
        }

        var service = new AspNetIdentityUserInfoService(db.DbContextFactory);
        var list = await service.GetAllUsersAsync();

        Assert.Equal(2, list.Count);
        Assert.Equal("bravo", list[0].Username);
        Assert.Equal("charlie", list[1].Username);
    }
}