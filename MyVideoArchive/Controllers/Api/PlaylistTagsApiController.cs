using MyVideoArchive.Models.Requests.Playlist;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for managing playlist tags
/// </summary>
[Authorize]
[ApiController]
[Route("api/playlists/{playlistId}/tags")]
public class PlaylistTagsApiController : ControllerBase
{
    private readonly ITagService tagService;

    public PlaylistTagsApiController(ITagService tagService)
    {
        this.tagService = tagService;
    }

    /// <summary>
    /// Get all tags on a playlist
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetPlaylistTags(int playlistId)
    {
        var result = await tagService.GetPlaylistTagsAsync(playlistId);

        return result.ToActionResult(this, value => Ok(new { tags = value.Tags }));
    }

    /// <summary>
    /// Set the tags for a playlist (replaces existing tags)
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> SetPlaylistTags(int playlistId, [FromBody] SetPlaylistTagsRequest request)
    {
        var result = await tagService.SetPlaylistTagsAsync(playlistId, request);

        return result.ToActionResult(this, () => Ok(new { message = "Tags updated" }));
    }
}
