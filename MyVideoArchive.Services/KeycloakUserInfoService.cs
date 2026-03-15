namespace MyVideoArchive.Services;

/// <summary>
/// Stub user info service used when <c>Authentication:Provider</c> is <c>Keycloak</c>.
/// Keycloak manages user profiles externally; without calling the Keycloak Admin API
/// (which requires separate admin credentials), the app has no access to usernames or
/// emails for arbitrary user IDs. This implementation returns the raw user ID as the
/// username so that subscriber lists remain functional.
/// </summary>
public class KeycloakUserInfoService : IUserInfoService
{
    public Task<IReadOnlyDictionary<string, UserInfo>> GetUserInfoAsync(
        IEnumerable<string> userIds,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, UserInfo> result = userIds
            .Distinct()
            .ToDictionary(id => id, id => new UserInfo(id, id, string.Empty));

        return Task.FromResult(result);
    }
}
