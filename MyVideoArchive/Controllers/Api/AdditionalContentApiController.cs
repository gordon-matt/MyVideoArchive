using MyVideoArchive.Models.Requests.AdditionalContent;
using MyVideoArchive.Services;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API for managing supplementary files (PDFs, images, archives, etc.) associated with channels,
/// playlists and videos. Any authenticated user can read/download; only administrators can
/// create, edit or delete items.
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

    [HttpGet("playlists/{playlistId:int}/additional-content")]
    public async Task<IActionResult> GetByPlaylist(int playlistId)
    {
        var result = await service.GetPlaylistItemsAsync(playlistId);
        return result.ToActionResult(this, items => Ok(new { items }));
    }

    [HttpGet("additional-content/{id:int}/download")]
    public async Task<IActionResult> Download(int id)
    {
        var result = await service.GetDownloadInfoAsync(id);
        if (!result.IsSuccess)
        {
            return result.Status == Ardalis.Result.ResultStatus.NotFound
                ? NotFound(new { message = "File not found." })
                : StatusCode(StatusCodes.Status500InternalServerError);
        }

        // Serve with the user-visible FileName so the browser prompts the correct name,
        // not the UUID-based stored file name.
        return PhysicalFile(result.Value.PhysicalPath, result.Value.ContentType, result.Value.DownloadFileName);
    }

    // ── Write (administrators only) ───────────────────────────────────────────

    [HttpPost("channels/{channelId:int}/additional-content")]
    [Authorize(Roles = Constants.Roles.Administrator)]
    [RequestSizeLimit(500 * 1024 * 1024)] // 500 MB
    public async Task<IActionResult> Upload(int channelId, IFormFile file, [FromForm] int? playlistId = null)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "No file provided." });
        }

        var result = await service.UploadAsync(channelId, file, playlistId);
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
}
