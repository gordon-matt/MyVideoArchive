using Hangfire;
using Microsoft.AspNetCore.Mvc;
using MyVideoArchive.Services.Jobs;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for channel operations like sync
/// </summary>
[ApiController]
[Route("api/channels")]
public class ChannelOperationsApiController : ControllerBase
{
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<ChannelOperationsApiController> _logger;

    public ChannelOperationsApiController(
        IBackgroundJobClient backgroundJobClient,
        ILogger<ChannelOperationsApiController> logger)
    {
        _backgroundJobClient = backgroundJobClient;
        _logger = logger;
    }

    /// <summary>
    /// Trigger sync for all channels
    /// </summary>
    [HttpPost("sync-all")]
    public IActionResult SyncAllChannels()
    {
        try
        {
            _backgroundJobClient.Enqueue<ChannelSyncJob>(job =>
                job.SyncAllChannelsAsync(CancellationToken.None));

            _logger.LogInformation("Queued sync job for all channels");

            return Ok(new
            {
                message = "Sync job queued successfully for all channels"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error queueing sync job for all channels");
            return StatusCode(500, new { message = "An error occurred while queueing sync job" });
        }
    }
}
