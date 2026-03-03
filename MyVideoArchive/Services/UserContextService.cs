using System.Security.Claims;

namespace MyVideoArchive.Services;

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