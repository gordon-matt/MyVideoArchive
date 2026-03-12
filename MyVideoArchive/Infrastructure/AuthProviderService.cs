namespace MyVideoArchive.Infrastructure;

/// <summary>
/// Indicates which authentication provider is active.
/// Configured via "Authentication:Provider" in appsettings.json or environment variables.
/// </summary>
public enum AuthProvider
{
    /// <summary>Built-in ASP.NET Core Identity (default).</summary>
    Identity,

    /// <summary>External Keycloak OpenID Connect provider.</summary>
    Keycloak
}

/// <summary>
/// Provides runtime information about the configured authentication provider.
/// Inject this into controllers and views to branch behaviour between Identity and Keycloak.
/// </summary>
public interface IAuthProviderService
{
    /// <summary>The active authentication provider.</summary>
    AuthProvider Provider { get; }

    /// <summary>True when Keycloak OIDC is the active provider.</summary>
    bool IsKeycloak { get; }

    /// <summary>True when ASP.NET Core Identity is the active provider.</summary>
    bool IsIdentity { get; }

    /// <summary>
    /// Base URL of the Keycloak account self-service console (e.g. manage password/profile).
    /// Null when not using Keycloak.
    /// </summary>
    string? KeycloakAccountUrl { get; }
}

/// <inheritdoc />
public sealed class AuthProviderService : IAuthProviderService
{
    public AuthProvider Provider { get; }
    public bool IsKeycloak => Provider == AuthProvider.Keycloak;
    public bool IsIdentity => Provider == AuthProvider.Identity;
    public string? KeycloakAccountUrl { get; }

    public AuthProviderService(AuthProvider provider, string? keycloakAuthority)
    {
        Provider = provider;

        if (provider == AuthProvider.Keycloak && !string.IsNullOrWhiteSpace(keycloakAuthority))
        {
            KeycloakAccountUrl = keycloakAuthority.TrimEnd('/') + "/account";
        }
    }
}
