using MyVideoArchive.Data.Enums;
using MyVideoArchive.Models.Api;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for persisting per-user UI preferences
/// </summary>
[Authorize]
[ApiController]
[Route("api/user/settings")]
public class UserSettingsApiController : ControllerBase
{
    private readonly ILogger<UserSettingsApiController> logger;
    private readonly IUserContextService userContextService;
    private readonly IRepository<UserSettingsEntry> userSettingsRepository;

    public UserSettingsApiController(
        ILogger<UserSettingsApiController> logger,
        IUserContextService userContextService,
        IRepository<UserSettingsEntry> userSettingsRepository)
    {
        this.logger = logger;
        this.userContextService = userContextService;
        this.userSettingsRepository = userSettingsRepository;
    }

    /// <summary>
    /// Get the current user's UI settings
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetSettings()
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var entry = await userSettingsRepository.FindOneAsync(new SearchOptions<UserSettingsEntry>
            {
                Query = x => x.UserId == userId
            });

            return Ok(new
            {
                videosTabViewMode = entry?.VideosTabViewMode.ToString().ToLowerInvariant() ?? "list",
                availableTabViewMode = entry?.AvailableTabViewMode.ToString().ToLowerInvariant() ?? "list"
            });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving user settings");
            }

            return StatusCode(500, new { message = "An error occurred while retrieving user settings" });
        }
    }

    /// <summary>
    /// Update the current user's UI settings
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateUserSettingsRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var entry = await userSettingsRepository.FindOneAsync(new SearchOptions<UserSettingsEntry>
            {
                Query = x => x.UserId == userId
            });

            if (entry is null)
            {
                entry = new UserSettingsEntry
                {
                    UserId = userId,
                    VideosTabViewMode = ParseViewMode(request.VideosTabViewMode),
                    AvailableTabViewMode = ParseViewMode(request.AvailableTabViewMode)
                };
                await userSettingsRepository.InsertAsync(entry);
            }
            else
            {
                if (request.VideosTabViewMode is not null)
                {
                    entry.VideosTabViewMode = ParseViewMode(request.VideosTabViewMode);
                }

                if (request.AvailableTabViewMode is not null)
                {
                    entry.AvailableTabViewMode = ParseViewMode(request.AvailableTabViewMode);
                }

                await userSettingsRepository.UpdateAsync(entry);
            }

            return Ok(new { message = "Settings saved" });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error updating user settings");
            }

            return StatusCode(500, new { message = "An error occurred while updating user settings" });
        }
    }

    private static ViewMode ParseViewMode(string? value) =>
        value?.ToLowerInvariant() == "grid" ? ViewMode.Grid : ViewMode.List;
}
