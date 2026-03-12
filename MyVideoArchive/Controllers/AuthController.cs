using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyVideoArchive.Infrastructure;

namespace MyVideoArchive.Controllers;

/// <summary>
/// Handles authentication flows when Keycloak (OIDC) is the active provider.
/// This controller is only reachable at runtime; when Identity is the active
/// provider the standard Identity UI razor pages are used instead.
/// </summary>
[Route("auth")]
public class AuthController : Controller
{
    private readonly IAuthProviderService _authProvider;

    public AuthController(IAuthProviderService authProvider)
    {
        _authProvider = authProvider;
    }

    /// <summary>
    /// Initiates an OIDC challenge, redirecting the browser to Keycloak's login page.
    /// </summary>
    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = "/")
    {
        if (!_authProvider.IsKeycloak)
            return NotFound();

        var props = new AuthenticationProperties { RedirectUri = returnUrl ?? "/" };
        return Challenge(props, OpenIdConnectDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Signs the user out of both the local cookie session and the remote Keycloak session
    /// (single sign-out). After logout Keycloak redirects back to the application root.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        if (!_authProvider.IsKeycloak)
            return NotFound();

        var props = new AuthenticationProperties { RedirectUri = "/" };
        return SignOut(
            props,
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }
}
