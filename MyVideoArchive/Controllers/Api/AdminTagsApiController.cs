namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for admin management of global tags
/// </summary>
[ApiController]
[Route("api/admin/tags")]
[Authorize(Roles = Constants.Roles.Administrator)]
public class AdminTagsApiController : ControllerBase
{
    private readonly ITagService tagService;

    public AdminTagsApiController(ITagService tagService)
    {
        this.tagService = tagService;
    }

    /// <summary>
    /// Get all global tags with their usage counts
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetGlobalTags()
    {
        var result = await tagService.GetGlobalTagsAsync();
        return result.ToActionResult(this, value => Ok(new { tags = value.Tags }));
    }

    /// <summary>
    /// Create a global tag. Any per-user tags with the same name are consolidated.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateGlobalTag([FromBody] CreateGlobalTagRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Name))
        {
            return BadRequest(new { message = "Tag name is required" });
        }

        var result = await tagService.CreateGlobalTagAsync(request.Name);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>
    /// Delete a global tag and all its VideoTag associations
    /// </summary>
    [HttpDelete("{tagId}")]
    public async Task<IActionResult> DeleteGlobalTag(int tagId)
    {
        var result = await tagService.DeleteGlobalTagAsync(tagId);
        return result.ToActionResult(this, () => Ok(new { message = "Tag deleted" }));
    }
}

public class CreateGlobalTagRequest
{
    public string? Name { get; set; }
}