using Ardalis.Result;

namespace MyVideoArchive.Services;

/// <summary>
/// Lets an admin check the installed yt-dlp version and trigger an update or rollback on demand
/// from the Admin UI, instead of waiting for the weekly auto-update job or a redeploy.
/// </summary>
public interface IYtDlpMaintenanceService
{
    /// <summary>
    /// Returns the currently installed yt-dlp version, whether an update/rollback is in
    /// progress, the outcome of the last one, and details of the available backup (if any).
    /// </summary>
    Task<object> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues an update to the latest yt-dlp release (the current install is backed up first).
    /// Returns AlreadyRunning if an update or rollback is already in progress.
    /// </summary>
    Result<YtDlpMaintenanceStartOutcome> RequestUpdate();

    /// <summary>
    /// Queues a rollback to the backup taken before the last update.
    /// Returns AlreadyRunning if an update or rollback is already in progress.
    /// </summary>
    Result<YtDlpMaintenanceStartOutcome> RequestRollback();
}
