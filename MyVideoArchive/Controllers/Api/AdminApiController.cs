using MyVideoArchive.Models;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for file system scan operations.
/// Scans run in background; poll /status for progress and call /cancel to stop early.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = Constants.Roles.Administrator)]
public class AdminApiController : ControllerBase
{
    private readonly IFileSystemScanService fileSystemScanService;
    private readonly IVideoService videoService;
    private readonly IAdminDashboardService adminDashboardService;

    public AdminApiController(
        IFileSystemScanService fileSystemScanService,
        IVideoService videoService,
        IAdminDashboardService adminDashboardService)
    {
        this.fileSystemScanService = fileSystemScanService;
        this.videoService = videoService;
        this.adminDashboardService = adminDashboardService;
    }

    /// <summary>
    /// Returns archive-wide statistics for the admin dashboard (home page for administrators).
    /// </summary>
    [HttpGet("dashboard-stats")]
    public async Task<IActionResult> GetDashboardStats(CancellationToken cancellationToken)
    {
        var result = await adminDashboardService.GetStatsAsync(cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>
    /// Starts a background file system scan across channels (see <paramref name="scope"/>).
    /// Returns 202 Accepted immediately; poll /status for progress.
    /// Returns 409 Conflict if a scan is already running.
    /// </summary>
    [HttpPost("scan-filesystem")]
    public async Task<IActionResult> ScanFileSystem(
        [FromQuery(Name = "scope")] FileSystemScanChannelScope scope = FileSystemScanChannelScope.All)
    {
        var result = await fileSystemScanService.StartScanAsync(scope);

        if (!result.IsSuccess)
        {
            return StatusCode(500, new { message = result.Errors.FirstOrDefault() ?? "An error occurred" });
        }

        return result.Value == ScanStartOutcome.AlreadyRunning
            ? Conflict(new { message = "A file system scan is already in progress." })
            : Accepted();
    }

    /// <summary>
    /// Starts a background file system scan for a single channel.
    /// Returns 202 Accepted immediately; poll /status for progress.
    /// Returns 409 Conflict if a scan is already running.
    /// </summary>
    [HttpPost("channels/{channelId:int}/scan-filesystem")]
    public async Task<IActionResult> ScanFileSystemByChannel(int channelId)
    {
        var result = await fileSystemScanService.StartChannelScanAsync(channelId);

        if (!result.IsSuccess)
        {
            return StatusCode(500, new { message = result.Errors.FirstOrDefault() ?? "An error occurred" });
        }

        return result.Value == ScanStartOutcome.AlreadyRunning
            ? Conflict(new { message = "A file system scan is already in progress." })
            : Accepted();
    }

    /// <summary>
    /// Returns the current status of any running or recently completed file system scan.
    /// </summary>
    [HttpGet("scan-filesystem/status")]
    public IActionResult GetScanStatus() => Ok(fileSystemScanService.GetStatus());

    /// <summary>
    /// Requests cancellation of the currently running file system scan.
    /// </summary>
    [HttpPost("scan-filesystem/cancel")]
    public IActionResult CancelScan()
    {
        var result = fileSystemScanService.Cancel();
        return result.ToActionResult(this, () => Ok(new { message = "Cancellation requested." }));
    }

    /// <summary>
    /// Retry fetching platform metadata for a video flagged as needing review.
    /// </summary>
    [HttpPost("videos/{videoId:int}/retry-metadata")]
    public async Task<IActionResult> RetryMetadata(int videoId, CancellationToken cancellationToken)
    {
        var result = await videoService.RetryMetadataAsync(videoId, cancellationToken);

        return result.ToActionResult(this, value => Ok(new { success = value.Success, message = value.Message }));
    }

    /// <summary>
    /// Returns all videos that have been flagged as DownloadFailed.
    /// </summary>
    [HttpGet("failed-downloads")]
    public async Task<IActionResult> GetFailedDownloads(CancellationToken cancellationToken)
    {
        var result = await videoService.GetFailedDownloadsAsync(cancellationToken);

        return result.ToActionResult(this, value => Ok(new { videos = value.Videos }));
    }

    /// <summary>
    /// Clears the failed flag for a video and re-queues its download.
    /// </summary>
    [HttpPost("videos/{videoId:int}/retry-download")]
    public async Task<IActionResult> RetryDownload(int videoId, CancellationToken cancellationToken)
    {
        var result = await videoService.RetryDownloadAsync(videoId, cancellationToken);

        return result.ToActionResult(this, value => Ok(new { success = value.Success, message = value.Message }));
    }
}