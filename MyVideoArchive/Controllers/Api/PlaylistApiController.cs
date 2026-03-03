using MyVideoArchive.Models.Api;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for playlist-specific operations (reordering, hiding, etc.)
/// </summary>
[Authorize]
[ApiController]
[Route("api/playlists")]
public class PlaylistApiController : ControllerBase
{
    private readonly IPlaylistService playlistService;

    public PlaylistApiController(IPlaylistService playlistService)
    {
        this.playlistService = playlistService;
    }

    /// <summary>
    /// Save custom video order for a playlist
    /// </summary>
    [HttpPost("{playlistId}/reorder")]
    public async Task<IActionResult> SaveCustomOrder(int playlistId, [FromBody] ReorderVideosRequest request)
    {
        var result = await playlistService.SaveCustomOrderAsync(playlistId, request);

        return result.ToActionResult(this, () => Ok(new { message = "Custom order saved successfully" }));
    }

    /// <summary>
    /// Get the current order setting and preference for a playlist
    /// </summary>
    [HttpGet("{playlistId}/order-setting")]
    public async Task<IActionResult> GetOrderSetting(int playlistId)
    {
        var result = await playlistService.GetOrderSettingAsync(playlistId);

        return result.ToActionResult(this, value => Ok(new { hasCustomOrder = value.HasCustomOrder, useCustomOrder = value.UseCustomOrder }));
    }

    /// <summary>
    /// Get custom video order for a playlist
    /// </summary>
    [HttpGet("{playlistId}/custom-order")]
    public async Task<IActionResult> GetCustomOrder(int playlistId)
    {
        var result = await playlistService.GetCustomOrderAsync(playlistId);

        return result.ToActionResult(this, value => Ok(new { videoOrders = value.VideoOrders }));
    }

    /// <summary>
    /// Get videos for a playlist with proper ordering and hidden-video awareness
    /// </summary>
    [HttpGet("{playlistId}/videos")]
    public async Task<IActionResult> GetPlaylistVideos(int playlistId, [FromQuery] bool useCustomOrder = false, [FromQuery] bool showHidden = false)
    {
        var result = await playlistService.GetPlaylistVideosAsync(playlistId, useCustomOrder, showHidden);

        return result.ToActionResult(this, value => Ok(new { videos = value.Videos }));
    }

    /// <summary>
    /// Set the hidden/visible status of a video within a playlist for the current user
    /// </summary>
    [HttpPut("{playlistId}/videos/{videoId}/hidden")]
    public async Task<IActionResult> SetVideoHidden(int playlistId, int videoId, [FromBody] SetVideoHiddenRequest request)
    {
        var result = await playlistService.SetVideoHiddenAsync(playlistId, videoId, request);

        return result.ToActionResult(this, () => Ok(new { message = request.IsHidden ? "Video hidden" : "Video unhidden" }));
    }

    /// <summary>
    /// Trigger sync for all playlists
    /// </summary>
    [Authorize(Roles = Constants.Roles.Administrator)]
    [HttpPost("sync-all")]
    public IActionResult SyncAllPlaylists()
    {
        var result = playlistService.SyncAllPlaylists();

        return result.ToActionResult(this, () => Ok(new { message = "Sync job queued successfully for all playlists" }));
    }
}