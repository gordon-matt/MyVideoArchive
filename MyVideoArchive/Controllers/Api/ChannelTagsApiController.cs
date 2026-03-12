using MyVideoArchive.Models.Requests.Channel;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for managing channel tags
/// </summary>
[Authorize]
[ApiController]
[Route("api/channels/{channelId}/tags")]
public class ChannelTagsApiController : ControllerBase
{
    private readonly ITagService tagService;

    public ChannelTagsApiController(ITagService tagService)
    {
        this.tagService = tagService;
    }

    /// <summary>
    /// Get all tags on a channel
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetChannelTags(int channelId)
    {
        var result = await tagService.GetChannelTagsAsync(channelId);

        return result.ToActionResult(this, value => Ok(new { tags = value.Tags }));
    }

    /// <summary>
    /// Set the tags for a channel (replaces existing tags)
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> SetChannelTags(int channelId, [FromBody] SetChannelTagsRequest request)
    {
        var result = await tagService.SetChannelTagsAsync(channelId, request);

        return result.ToActionResult(this, () => Ok(new { message = "Tags updated" }));
    }
}
