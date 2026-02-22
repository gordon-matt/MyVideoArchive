using Hangfire;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for channel operations like sync
/// </summary>
[ApiController]
[Route("api/channels")]
public class ChannelOperationsApiController : ControllerBase
{
    private readonly ILogger<ChannelOperationsApiController> logger;
    private readonly IBackgroundJobClient backgroundJobClient;

    public ChannelOperationsApiController(
        ILogger<ChannelOperationsApiController> logger,
        IBackgroundJobClient backgroundJobClient)
    {
        this.logger = logger;
        this.backgroundJobClient = backgroundJobClient;
    }

    /// <summary>
    /// Trigger sync for all channels
    /// </summary>
    [HttpPost("sync-all")]
    public IActionResult SyncAllChannels()
    {
        try
        {
            backgroundJobClient.Enqueue<ChannelSyncJob>(job =>
                job.SyncAllChannelsAsync(CancellationToken.None));

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Queued sync job for all channels");
            }

            return Ok(new
            {
                message = "Sync job queued successfully for all channels"
            });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error queueing sync job for all channels");
            }

            return StatusCode(500, new { message = "An error occurred while queueing sync job" });
        }
    }
}