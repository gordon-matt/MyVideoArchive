namespace MyVideoArchive.Services.Content;

/// <summary>
/// Which yt-dlp maintenance action a job/status entry refers to.
/// </summary>
public enum YtDlpMaintenanceOperation
{
    Update,
    Rollback
}

/// <summary>
/// Outcome of requesting an update or rollback: either it was queued, or one was already
/// in progress and the request was ignored.
/// </summary>
public enum YtDlpMaintenanceStartOutcome
{
    Started,
    AlreadyRunning
}

/// <summary>
/// Describes the yt-dlp install that was backed up immediately before the last update, so it can
/// be restored by a rollback. Persisted as JSON alongside the backup (see
/// <see cref="YtDlpBackupManager"/>).
/// </summary>
/// <param name="Method">
/// The update method in effect when the backup was taken: "self" or "binary" (standalone
/// executable — restored via file copy) or "pip" (restored by reinstalling the pinned
/// <paramref name="Version"/>).
/// </param>
/// <param name="Version">yt-dlp's reported version at backup time, or null if it could not be read.</param>
/// <param name="BackedUpAtUtc">When the backup was taken.</param>
/// <param name="PipPackage">The pip package spec in use (e.g. <c>yt-dlp[default]</c>); only meaningful for the "pip" method.</param>
public sealed record YtDlpBackupMetadata(
    string Method,
    string? Version,
    DateTime BackedUpAtUtc,
    string? PipPackage);

/// <summary>
/// Outcome of a single update or rollback run, surfaced to the admin UI.
/// </summary>
public sealed record YtDlpMaintenanceResult(
    YtDlpMaintenanceOperation Operation,
    bool Success,
    string Message,
    string? VersionBefore,
    string? VersionAfter,
    DateTime CompletedAtUtc);
