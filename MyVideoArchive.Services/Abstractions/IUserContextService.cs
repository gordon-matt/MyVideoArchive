namespace MyVideoArchive.Services;

/// <summary>
/// Service for getting current user information
/// </summary>
public interface IUserContextService
{
    string? GetCurrentUserId();

    bool IsAdministrator();
}