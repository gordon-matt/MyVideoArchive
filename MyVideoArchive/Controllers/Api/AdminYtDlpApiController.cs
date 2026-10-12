namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for admin-triggered yt-dlp maintenance. Lets the admin pick up a new yt-dlp
/// release the moment YouTube (or another platform) breaks extraction, without waiting for a
/// redeploy — and roll back to the previous version in one click if the new release causes
/// problems. See <see cref="IYtDlpMaintenanceService"/> and
/// <see cref="MyVideoArchive.Services.Jobs.YtDlpUpdateJob"/> for the update/backup/rollback logic.
/// </summary>
[ApiController]
[Route("api/admin/yt-dlp")]
[Authorize(Roles = Constants.Roles.Administrator)]
public class AdminYtDlpApiController : ControllerBase
{
    private readonly IYtDlpMaintenanceService maintenanceService;

    public AdminYtDlpApiController(IYtDlpMaintenanceService maintenanceService)
    {
        this.maintenanceService = maintenanceService;
    }

    /// <summary>
    /// Returns the currently installed yt-dlp version, whether an update/rollback is in
    /// progress, the outcome of the last one, and details of the available backup (if any).
    /// Poll this after <see cref="Update"/> / <see cref="Rollback"/> to track progress.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
        => Ok(await maintenanceService.GetStatusAsync(cancellationToken));

    /// <summary>
    /// Triggers an update to the latest yt-dlp release. The current install is backed up first
    /// so it can be restored via <see cref="Rollback"/>. Runs in the background, serialized with
    /// any in-flight download; returns 202 Accepted immediately, or 409 if an update/rollback is
    /// already in progress.
    /// </summary>
    [HttpPost("update")]
    public IActionResult Update()
    {
        var result = maintenanceService.RequestUpdate();
        return ToStartActionResult(result);
    }

    /// <summary>
    /// Restores yt-dlp from the backup taken before the last update. Returns 202 Accepted
    /// immediately, or 409 if an update/rollback is already in progress.
    /// </summary>
    [HttpPost("rollback")]
    public IActionResult Rollback()
    {
        var result = maintenanceService.RequestRollback();
        return ToStartActionResult(result);
    }

    private IActionResult ToStartActionResult(Ardalis.Result.Result<YtDlpMaintenanceStartOutcome> result)
    {
        if (!result.IsSuccess)
        {
            return result.ToActionResult(this, _ => Ok());
        }

        return result.Value == YtDlpMaintenanceStartOutcome.AlreadyRunning
            ? Conflict(new { message = "An update or rollback is already in progress." })
            : Accepted();
    }
}
