namespace MyVideoArchive.Tests.Services;

public class KeycloakUserInfoServiceTests
{
    [Fact]
    public async Task GetUserInfoAsync_WhenNoIds_ReturnsEmptyDictionary()
    {
        var service = new KeycloakUserInfoService(
            new ConfigurationBuilder().Build(),
            NullLogger<KeycloakUserInfoService>.Instance);

        var map = await service.GetUserInfoAsync([]);

        Assert.Empty(map);
    }

    [Fact]
    public async Task GetAllUsersAsync_WhenKeycloakNotConfigured_ReturnsEmptyList()
    {
        var service = new KeycloakUserInfoService(
            new ConfigurationBuilder().Build(),
            NullLogger<KeycloakUserInfoService>.Instance);

        var list = await service.GetAllUsersAsync();

        Assert.Empty(list);
    }
}