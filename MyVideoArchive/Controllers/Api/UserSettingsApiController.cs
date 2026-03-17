using MyVideoArchive.Models.Requests;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for persisting per-user UI preferences
/// </summary>
[Authorize]
[ApiController]
[Route("api/user/settings")]
public class UserSettingsApiController : ControllerBase
{
    private readonly IUserSettingsService userSettingsService;

    public UserSettingsApiController(IUserSettingsService userSettingsService)
    {
        this.userSettingsService = userSettingsService;
    }

    /// <summary>
    /// Get the current user's UI settings
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetSettings()
    {
        var result = await userSettingsService.GetSettingsAsync();

        return result.ToActionResult(this, value => Ok(new
        {
            videosTabViewMode = value.VideosTabViewMode,
            availableTabViewMode = value.AvailableTabViewMode,
            enableChannelCategories = value.EnableChannelCategories
        }));
    }

    /// <summary>
    /// Update the current user's UI settings
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateUserSettingsRequest request)
    {
        var result = await userSettingsService.UpdateSettingsAsync(request);

        return result.ToActionResult(this, () => Ok(new { message = "Settings saved" }));
    }
}