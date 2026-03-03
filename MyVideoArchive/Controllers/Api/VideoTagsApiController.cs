using MyVideoArchive.Models.Video;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for managing video tags
/// </summary>
[Authorize]
[ApiController]
[Route("api/videos/{videoId}/tags")]
public class VideoTagsApiController : ControllerBase
{
    private readonly ITagService tagService;

    public VideoTagsApiController(ITagService tagService)
    {
        this.tagService = tagService;
    }

    /// <summary>
    /// Get all tags applied to a video for the current user
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetVideoTags(int videoId)
    {
        var result = await tagService.GetVideoTagsAsync(videoId);

        return result.ToActionResult(this, value => Ok(new { tags = value.Tags }));
    }

    /// <summary>
    /// Set the tags for a video (replaces existing tags for this user)
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> SetVideoTags(int videoId, [FromBody] SetVideoTagsRequest request)
    {
        var result = await tagService.SetVideoTagsAsync(videoId, request);

        return result.ToActionResult(this, () => Ok(new { message = "Tags updated" }));
    }
}