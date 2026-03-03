using Ardalis.Result;
using MyVideoArchive.Models.Requests.Channel;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for creating and managing custom (non-platform) channels, playlists and videos
/// </summary>
[ApiController]
[Route("api/custom")]
[Authorize]
public class CustomChannelApiController : ControllerBase
{
    private readonly ICustomChannelService customChannelService;

    public CustomChannelApiController(ICustomChannelService customChannelService)
    {
        this.customChannelService = customChannelService;
    }

    [HttpPost("channels")]
    public async Task<IActionResult> CreateChannel([FromBody] CreateCustomChannelRequest request)
    {
        var result = await customChannelService.CreateChannelAsync(request);
        return result.ToActionResult(this, value => Ok(new { value.Id, value.Name, value.Platform }));
    }

    [HttpPut("channels/{channelId:int}")]
    public async Task<IActionResult> UpdateChannel(int channelId, [FromBody] UpdateCustomChannelRequest request)
    {
        var result = await customChannelService.UpdateChannelAsync(channelId, request);
        return result.ToActionResult(this, Ok);
    }

    [HttpGet("channels/{channelId:int}/thumbnail")]
    public async Task<IActionResult> GetChannelThumbnail(int channelId)
    {
        var result = await customChannelService.GetChannelThumbnailAsync(channelId);
        return !result.IsSuccess
            ? result.Status == ResultStatus.NotFound ? NotFound() : Forbid()
            : PhysicalFile(result.Value.PhysicalPath, result.Value.ContentType);
    }

    [HttpPost("channels/{channelId:int}/thumbnail")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadChannelThumbnail(int channelId, IFormFile file)
    {
        await using var stream = file.OpenReadStream();
        var result = await customChannelService.UploadChannelThumbnailAsync(channelId, stream, file.FileName);
        return result.ToActionResult(this, thumbnailUrl => Ok(new { thumbnailUrl }));
    }

    [HttpPost("channels/{channelId:int}/playlists")]
    public async Task<IActionResult> CreatePlaylist(int channelId, [FromBody] CreateCustomChannelPlaylistRequest request)
    {
        var result = await customChannelService.CreatePlaylistAsync(channelId, request);
        return result.ToActionResult(this, value => Ok(new { value.Id, value.Name }));
    }

    [HttpPut("playlists/{playlistId:int}")]
    public async Task<IActionResult> UpdatePlaylist(int playlistId, [FromBody] UpdateCustomChannelPlaylistRequest request)
    {
        var result = await customChannelService.UpdatePlaylistAsync(playlistId, request);
        return result.ToActionResult(this, Ok);
    }

    [HttpDelete("playlists/{playlistId:int}")]
    public async Task<IActionResult> DeletePlaylist(int playlistId)
    {
        var result = await customChannelService.DeletePlaylistAsync(playlistId);
        return result.ToActionResult(this, NoContent);
    }

    [HttpGet("playlists/{playlistId:int}/thumbnail")]
    public async Task<IActionResult> GetPlaylistThumbnail(int playlistId)
    {
        var result = await customChannelService.GetPlaylistThumbnailAsync(playlistId);
        return !result.IsSuccess ? NotFound() : PhysicalFile(result.Value.PhysicalPath, result.Value.ContentType);
    }

    [HttpPost("playlists/{playlistId:int}/thumbnail")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadPlaylistThumbnail(int playlistId, IFormFile file)
    {
        await using var stream = file.OpenReadStream();
        var result = await customChannelService.UploadPlaylistThumbnailAsync(playlistId, stream, file.FileName);
        return result.ToActionResult(this, thumbnailUrl => Ok(new { thumbnailUrl }));
    }

    [HttpGet("channels/{channelId:int}/playlists")]
    public async Task<IActionResult> GetChannelPlaylists(int channelId)
    {
        var result = await customChannelService.GetChannelPlaylistsAsync(channelId);
        return result.ToActionResult(this, value => Ok(value.Playlists));
    }

    [HttpGet("videos/{videoId:int}/playlists")]
    public async Task<IActionResult> GetVideoPlaylists(int videoId)
    {
        var result = await customChannelService.GetVideoPlaylistIdsAsync(videoId);
        return result.ToActionResult(this, value => Ok(value.PlaylistIds));
    }

    [HttpPut("videos/{videoId:int}")]
    public async Task<IActionResult> UpdateVideo(int videoId, [FromBody] UpdateCustomVideoRequest request)
    {
        var result = await customChannelService.UpdateVideoAsync(videoId, request);
        return result.ToActionResult(this, Ok);
    }

    [HttpGet("videos/{videoId:int}/thumbnail")]
    public async Task<IActionResult> GetVideoThumbnail(int videoId)
    {
        var result = await customChannelService.GetVideoThumbnailAsync(videoId);
        return !result.IsSuccess ? NotFound() : PhysicalFile(result.Value.PhysicalPath, result.Value.ContentType);
    }

    [HttpPost("videos/{videoId:int}/thumbnail")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadVideoThumbnail(int videoId, IFormFile file)
    {
        await using var stream = file.OpenReadStream();
        var result = await customChannelService.UploadVideoThumbnailAsync(videoId, stream, file.FileName);
        return result.ToActionResult(this, thumbnailUrl => Ok(new { thumbnailUrl }));
    }
}