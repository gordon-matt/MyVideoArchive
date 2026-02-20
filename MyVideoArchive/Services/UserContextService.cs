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
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UserContextService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? GetCurrentUserId() => _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

    public bool IsAdministrator() => _httpContextAccessor.HttpContext?.User?.IsInRole("Administrator") ?? false;
}
