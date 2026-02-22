using Hangfire;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for triggering playlist sync operations
/// </summary>
[ApiController]
[Route("api/playlists")]
[Authorize(Roles = Constants.Roles.Administrator)]
public class PlaylistSyncApiController : ControllerBase
{
    private readonly ILogger<PlaylistSyncApiController> logger;
    private readonly IBackgroundJobClient backgroundJobClient;

    public PlaylistSyncApiController(
        ILogger<PlaylistSyncApiController> logger,
        IBackgroundJobClient backgroundJobClient)
    {
        this.logger = logger;
        this.backgroundJobClient = backgroundJobClient;
    }

    /// <summary>
    /// Trigger sync for all playlists
    /// </summary>
    [HttpPost("sync-all")]
    public IActionResult SyncAllPlaylists()
    {
        try
        {
            backgroundJobClient.Enqueue<PlaylistSyncJob>(job =>
                job.SyncAllPlaylistsAsync(CancellationToken.None));

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Queued sync job for all playlists");
            }

            return Ok(new { message = "Sync job queued successfully for all playlists" });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error queueing sync job for all playlists");
            }

            return StatusCode(500, new { message = "An error occurred while queueing the sync job" });
        }
    }
}