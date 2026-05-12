using Ardalis.Result;
using Extenso;
using MyVideoArchive.Models.Requests.AdditionalContent;
using MyVideoArchive.Services;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API for managing supplementary files (PDFs, images, archives, etc.) associated with channels,
/// playlists and videos. Any authenticated user can read/download; only administrators can
/// create, edit or delete items (or change associations).
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
public class AdditionalContentApiController : ControllerBase
{
    private readonly IAdditionalContentService service;

    public AdditionalContentApiController(IAdditionalContentService service)
    {
        this.service = service;
    }

    // ── Read (any authenticated user) ─────────────────────────────────────────

    [HttpGet("channels/{channelId:int}/additional-content")]
    public async Task<IActionResult> GetByChannel(int channelId)
    {
        var result = await service.GetChannelItemsAsync(channelId);
        return result.ToActionResult(this, items => Ok(new { items }));
    }

    [HttpGet("videos/{videoId:int}/additional-content")]
    public async Task<IActionResult> GetByVideo(int videoId)
    {
        var result = await service.GetItemsForVideoAsync(videoId);
        return result.ToActionResult(this, items => Ok(new { items }));
    }

    [HttpGet("playlists/{playlistId:int}/videos/{videoId:int}/additional-content/available")]
    public async Task<IActionResult> GetAvailableForVideoOnPlaylist(int playlistId, int videoId)
    {
        var result = await service.GetAvailableItemsForVideoOnPlaylistAsync(playlistId, videoId);
        return result.ToActionResult(this, items => Ok(new { items }));
    }

    /// <summary>
    /// Serves the raw file. Use <paramref name="isForDownload"/> = true to suggest a filename (attachment);
    /// omit or false to open inline in the browser (e.g. images, PDFs) when possible.
    /// </summary>
    [HttpGet("additional-content/{id:int}/download")]
    public async Task<IActionResult> GetFile(int id, [FromQuery] bool isForDownload = false)
    {
        var result = await service.GetDownloadInfoAsync(id);
        if (!result.IsSuccess)
        {
            return result.Status == ResultStatus.NotFound
                ? NotFound(new { message = "File not found." })
                : StatusCode(StatusCodes.Status500InternalServerError);
        }

        var canOpenInline =
            result.Value.ContentType.StartsWith("image/") ||
            result.Value.ContentType.In("application/pdf", "text/plain");

        var info = result.Value;
        return isForDownload || !canOpenInline
            ? PhysicalFile(info.PhysicalPath, info.ContentType, info.DownloadFileName)
            : PhysicalFile(info.PhysicalPath, info.ContentType);
    }

    // ── Write (administrators only) ───────────────────────────────────────────

    [HttpPost("channels/{channelId:int}/additional-content")]
    [Authorize(Roles = Constants.Roles.Administrator)]
    [RequestSizeLimit(500 * 1024 * 1024)] // 500 MB
    public async Task<IActionResult> Upload(int channelId, IFormFile file, [FromForm] int[]? playlistIds = null)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "No file provided." });
        }

        IReadOnlyList<int>? ids = playlistIds is { Length: > 0 } ? playlistIds : null;
        var result = await service.UploadAsync(channelId, file, ids);
        return result.ToActionResult(this, item => Ok(new { item }));
    }

    [HttpPut("additional-content/{id:int}")]
    [Authorize(Roles = Constants.Roles.Administrator)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateAdditionalContentRequest request)
    {
        var result = await service.UpdateAsync(id, request);
        return result.ToActionResult(this, NoContent);
    }

    [HttpDelete("additional-content/{id:int}")]
    [Authorize(Roles = Constants.Roles.Administrator)]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await service.DeleteAsync(id);
        return result.ToActionResult(this, NoContent);
    }

    [HttpPost("playlists/{playlistId:int}/videos/{videoId:int}/additional-content")]
    [Authorize(Roles = Constants.Roles.Administrator)]
    public async Task<IActionResult> LinkToVideo(int playlistId, int videoId, [FromBody] LinkAdditionalContentToVideoRequest request)
    {
        var result = await service.LinkItemsToVideoAsync(videoId, playlistId, request);
        return result.ToActionResult(this, NoContent);
    }

    [HttpDelete("videos/{videoId:int}/additional-content/{itemId:int}")]
    [Authorize(Roles = Constants.Roles.Administrator)]
    public async Task<IActionResult> UnlinkFromVideo(int videoId, int itemId)
    {
        var result = await service.UnlinkItemFromVideoAsync(videoId, itemId);
        return result.ToActionResult(this, NoContent);
    }
}
