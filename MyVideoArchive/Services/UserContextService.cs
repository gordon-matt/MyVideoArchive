using System.Security.Claims;

namespace MyVideoArchive.Services;

/// <summary>
/// Service for getting current user information
/// </summary>
public interface IUserContextService
{
    string? GetCurrentUserId();

    bool IsAdministrator();
}

public class UserContextService : IUserContextService
{
    private readonly IHttpContextAccessor httpContextAccessor;

    public UserContextService(IHttpContextAccessor httpContextAccessor)
    {
        this.httpContextAccessor = httpContextAccessor;
    }

    public string? GetCurrentUserId() => httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

    public bool IsAdministrator() => httpContextAccessor.HttpContext?.User?.IsInRole("Administrator") ?? false;
}