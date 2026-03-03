namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for triggering playlist sync operations
/// </summary>
[ApiController]
[Route("api/playlists")]
[Authorize(Roles = Constants.Roles.Administrator)]
public class PlaylistSyncApiController : ControllerBase
{
    private readonly IPlaylistService playlistService;

    public PlaylistSyncApiController(IPlaylistService playlistService)
    {
        this.playlistService = playlistService;
    }

    /// <summary>
    /// Trigger sync for all playlists
    /// </summary>
    [HttpPost("sync-all")]
    public IActionResult SyncAllPlaylists()
    {
        var result = playlistService.SyncAllPlaylists();

        return result.ToActionResult(this, () => Ok(new { message = "Sync job queued successfully for all playlists" }));
    }
}