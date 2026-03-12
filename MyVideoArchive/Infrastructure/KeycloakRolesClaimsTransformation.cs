using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace MyVideoArchive.Infrastructure;

/// <summary>
/// Extracts application roles from Keycloak's JWT token and adds them as standard
/// <see cref="ClaimTypes.Role"/> claims so that <c>[Authorize(Roles = "...")]</c>
/// attributes and <c>User.IsInRole()</c> checks work without modification.
///
/// Keycloak emits realm-level roles inside a nested <c>realm_access.roles</c> JSON object,
/// which ASP.NET Core's OIDC middleware does not automatically expand into individual role
/// claims. This transformation handles that expansion on every authenticated request.
///
/// Only roles that match the application's defined roles (Administrator, User) are added;
/// built-in Keycloak roles such as <c>offline_access</c> and <c>uma_authorization</c>
/// are silently ignored.
/// </summary>
public class KeycloakRolesClaimsTransformation : IClaimsTransformation
{
    private static readonly HashSet<string> _appRoles =
    [
        Constants.Roles.Administrator,
        Constants.Roles.User
    ];

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
            return Task.FromResult(principal);

        AddRolesFromRealmAccess(principal, identity);

        return Task.FromResult(principal);
    }

    private static void AddRolesFromRealmAccess(ClaimsPrincipal principal, ClaimsIdentity identity)
    {
        var realmAccessClaim = principal.FindFirst("realm_access");
        if (realmAccessClaim is null)
            return;

        try
        {
            using var doc = JsonDocument.Parse(realmAccessClaim.Value);

            if (!doc.RootElement.TryGetProperty("roles", out var rolesElement))
                return;

            foreach (var roleElement in rolesElement.EnumerateArray())
            {
                var roleName = roleElement.GetString();

                if (string.IsNullOrEmpty(roleName) || !_appRoles.Contains(roleName))
                    continue;

                if (!principal.HasClaim(ClaimTypes.Role, roleName))
                    identity.AddClaim(new Claim(ClaimTypes.Role, roleName));
            }
        }
        catch (JsonException)
        {
            // Malformed claim — skip silently.
        }
    }
}
