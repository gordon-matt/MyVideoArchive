namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for retrieving user-level tag lists (for Tagify autocomplete)
/// </summary>
[Authorize]
[ApiController]
[Route("api/tags")]
public class UserTagsApiController : ControllerBase
{
    private readonly ITagService tagService;

    public UserTagsApiController(ITagService tagService)
    {
        this.tagService = tagService;
    }

    /// <summary>
    /// Get all tag names for the current user (for autocomplete)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetUserTags()
    {
        var result = await tagService.GetUserTagsAsync();

        return result.ToActionResult(this, value => Ok(new { tags = value.Tags }));
    }
}