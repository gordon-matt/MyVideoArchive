using MyVideoArchive.Models.Requests.Playlist;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for managing user custom playlists
/// </summary>
[Authorize]
[ApiController]
[Route("api/custom-playlists")]
public class CustomPlaylistsApiController : ControllerBase
{
    private readonly ICustomPlaylistService customPlaylistService;
    private readonly ITagService tagService;

    public CustomPlaylistsApiController(ICustomPlaylistService customPlaylistService, ITagService tagService)
    {
        this.customPlaylistService = customPlaylistService;
        this.tagService = tagService;
    }

    [HttpPost("{id}/videos/{videoId}")]
    public async Task<IActionResult> AddVideoToPlaylist(int id, int videoId)
    {
        var result = await customPlaylistService.AddVideoToPlaylistAsync(id, videoId);
        return result.ToActionResult(this, () => Ok(new { message = "Video added to playlist" }));
    }

    [HttpPost]
    public async Task<IActionResult> CreatePlaylist([FromBody] CreateCustomPlaylistRequest request)
    {
        var result = await customPlaylistService.CreatePlaylistAsync(request);
        return result.ToActionResult(this, value => Ok(new { id = value.Id, name = value.Name }));
    }

    [HttpPost("preview")]
    public async Task<IActionResult> PreviewPlaylist([FromBody] PreviewPlaylistRequest request)
    {
        var result = await customPlaylistService.PreviewPlaylistAsync(request, HttpContext.RequestAborted);
        return result.ToActionResult(this, value => Ok(new
        {
            name = value.Name,
            description = value.Description,
            thumbnailUrl = value.ThumbnailUrl,
            platform = value.Platform,
            videos = value.Videos
        }));
    }

    [HttpPost("clone")]
    public async Task<IActionResult> ClonePlaylist([FromBody] ClonePlaylistRequest request)
    {
        var result = await customPlaylistService.ClonePlaylistAsync(request, HttpContext.RequestAborted);
        return result.ToActionResult(this, value => Ok(new
        {
            id = value.Id,
            name = value.Name,
            totalVideos = value.TotalVideos,
            newVideos = value.NewVideos,
            alreadyInLibrary = value.AlreadyInLibrary
        }));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePlaylist(int id)
    {
        var result = await customPlaylistService.DeletePlaylistAsync(id);
        return result.ToActionResult(this, NoContent);
    }

    [HttpGet]
    public async Task<IActionResult> GetPlaylists([FromQuery] int page = 1, [FromQuery] int pageSize = 60)
    {
        var result = await customPlaylistService.GetPlaylistsAsync(page, pageSize);
        return result.ToActionResult(this, value => Ok(new
        {
            playlists = value.Playlists,
            pagination = new
            {
                currentPage = value.CurrentPage,
                pageSize = value.PageSize,
                totalCount = value.TotalCount,
                totalPages = value.TotalPages
            }
        }));
    }

    [HttpGet("for-video/{videoId:int}")]
    public async Task<IActionResult> GetPlaylistsForVideo(int videoId)
    {
        var result = await customPlaylistService.GetPlaylistsForVideoAsync(videoId);
        return result.ToActionResult(this, value => Ok(new { playlists = value.Playlists }));
    }

    [HttpGet("{id}/thumbnail")]
    public async Task<IActionResult> GetPlaylistThumbnail(int id)
    {
        var result = await customPlaylistService.GetPlaylistThumbnailAsync(id);
        return !result.IsSuccess ? NotFound() : PhysicalFile(result.Value.PhysicalPath, result.Value.ContentType);
    }

    [HttpGet("{id}/videos")]
    public async Task<IActionResult> GetPlaylistVideos(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 60)
    {
        var result = await customPlaylistService.GetPlaylistVideosAsync(id, page, pageSize);
        return result.ToActionResult(this, value => Ok(new
        {
            playlist = value.Playlist,
            videos = value.Videos,
            pagination = new
            {
                currentPage = value.CurrentPage,
                pageSize = value.PageSize,
                totalCount = value.TotalCount,
                totalPages = value.TotalPages
            }
        }));
    }

    [HttpDelete("{id}/videos/{videoId}")]
    public async Task<IActionResult> RemoveVideoFromPlaylist(int id, int videoId)
    {
        var result = await customPlaylistService.RemoveVideoFromPlaylistAsync(id, videoId);
        return result.ToActionResult(this, NoContent);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePlaylist(int id, [FromBody] CreateCustomPlaylistRequest request)
    {
        var result = await customPlaylistService.UpdatePlaylistAsync(id, request);
        return result.ToActionResult(this, value => Ok(new { id = value.Id, name = value.Name }));
    }

    /// <summary>
    /// Get all tags on a custom playlist
    /// </summary>
    [HttpGet("{id}/tags")]
    public async Task<IActionResult> GetCustomPlaylistTags(int id)
    {
        var result = await tagService.GetCustomPlaylistTagsAsync(id);
        return result.ToActionResult(this, value => Ok(new { tags = value.Tags }));
    }

    /// <summary>
    /// Set the tags for a custom playlist (replaces existing tags)
    /// </summary>
    [HttpPut("{id}/tags")]
    public async Task<IActionResult> SetCustomPlaylistTags(int id, [FromBody] SetPlaylistTagsRequest request)
    {
        var result = await tagService.SetCustomPlaylistTagsAsync(id, request);
        return result.ToActionResult(this, () => Ok(new { message = "Tags updated" }));
    }

    [HttpPost("{id}/thumbnail")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadThumbnail(int id, IFormFile file)
    {
        await using var stream = file.OpenReadStream();
        var result = await customPlaylistService.UploadThumbnailAsync(id, stream, file.FileName);
        return result.ToActionResult(this, thumbnailUrl => Ok(new { thumbnailUrl }));
    }
}