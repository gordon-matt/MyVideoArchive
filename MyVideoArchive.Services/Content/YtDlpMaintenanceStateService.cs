namespace MyVideoArchive.Services.Content;

/// <summary>
/// In-memory, process-wide tracker for the currently running (or most recently completed) yt-dlp
/// update/rollback, so the admin UI can show live progress and the last outcome without every
/// request having to reach into Hangfire. Mirrors the pattern used by
/// <see cref="FileSystemScanStateService"/> for file system scans.
/// </summary>
public sealed class YtDlpMaintenanceStateService
{
    private readonly object syncLock = new();

    public bool IsRunning { get; private set; }

    public YtDlpMaintenanceOperation? CurrentOperation { get; private set; }

    public YtDlpMaintenanceResult? LastResult { get; private set; }

    /// <summary>
    /// Marks an operation as started if none is currently running. Returns false (and does
    /// nothing) if an update or rollback is already in progress.
    /// </summary>
    public bool TryStart(YtDlpMaintenanceOperation operation)
    {
        lock (syncLock)
        {
            if (IsRunning)
            {
                return false;
            }

            IsRunning = true;
            CurrentOperation = operation;
            return true;
        }
    }

    /// <summary>
    /// Marks an operation as started if none is currently running; a no-op otherwise. Used as a
    /// defensive fallback by the job methods for triggers that don't route through
    /// <c>YtDlpMaintenanceService</c> (e.g. invoked directly from the Hangfire dashboard), so
    /// <see cref="CurrentOperation"/> still reflects the right kind of work while it runs.
    /// </summary>
    public void EnsureStarted(YtDlpMaintenanceOperation operation) => TryStart(operation);

    /// <summary>
    /// Records the outcome of the operation started by <see cref="TryStart"/> and clears the
    /// running flag, regardless of whether it succeeded.
    /// </summary>
    public void Complete(YtDlpMaintenanceResult result)
    {
        lock (syncLock)
        {
            IsRunning = false;
            CurrentOperation = null;
            LastResult = result;
        }
    }
}
