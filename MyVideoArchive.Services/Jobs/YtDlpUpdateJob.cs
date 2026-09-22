using Hangfire;
using MyVideoArchive.Infrastructure;
using YoutubeDLSharp;
using static MyVideoArchive.Services.Content.YtDlpProcessHelper;

namespace MyVideoArchive.Services.Jobs;

// Note: MyVideoArchive.Services.Content (YtDlpBackupManager, YtDlpMaintenanceStateService,
// YtDlpMaintenanceOperation, YtDlpMaintenanceResult) is a project-wide global using — no extra
// `using` needed here, see ProjectUsings.cs.

/// <summary>
/// Hangfire job that keeps the yt-dlp binary up to date.
///
/// YouTube regularly changes its internal APIs, and an out-of-date yt-dlp starts failing
/// channel/playlist/video fetches with errors like "HTTP Error 400: Bad Request" /
/// "Request contains an invalid argument". yt-dlp itself prints a warning once its build is
/// older than 90 days. This job checks for and installs the latest release so archiving keeps
/// working without manual intervention. It can also be triggered on demand — either from the
/// Hangfire dashboard (/hangfire) or, more conveniently, the "yt-dlp" card on the Admin → Tools
/// tab, which also exposes a one-click rollback (see <see cref="ExecuteManualUpdateAsync"/> /
/// <see cref="ExecuteRollbackAsync"/>).
///
/// Before every update (scheduled or manual) the currently-installed yt-dlp is backed up via
/// <see cref="YtDlpBackupManager"/>, so a bad release can always be rolled back to the last
/// known-good version without redeploying the app.
///
/// Runs in the dedicated "downloads" queue so it serialises with <see cref="VideoDownloadJob"/>
/// and <see cref="SubtitleBackfillJob"/> — this avoids swapping the yt-dlp binary out from under
/// an in-flight download (on Windows, replacing a running executable fails outright).
/// </summary>
[Queue("downloads")]
public class YtDlpUpdateJob
{
    private readonly ILogger<YtDlpUpdateJob> logger;
    private readonly IConfiguration configuration;
    private readonly YoutubeDL ytdl;
    private readonly YtDlpBackupManager backupManager;
    private readonly YtDlpMaintenanceStateService maintenanceState;

    public YtDlpUpdateJob(
        ILogger<YtDlpUpdateJob> logger,
        IConfiguration configuration,
        YoutubeDL ytdl,
        YtDlpBackupManager backupManager,
        YtDlpMaintenanceStateService maintenanceState)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.ytdl = ytdl;
        this.backupManager = backupManager;
        this.maintenanceState = maintenanceState;
    }

    /// <summary>
    /// Recurring (weekly) entry point. No-ops when <c>YoutubeDL:AutoUpdate:Enabled</c> is false —
    /// checked at execution time so toggling the flag in appsettings takes effect on the next run
    /// without a redeploy.
    /// </summary>
    [HangfireSkipWhenPreviousInstanceIsRunningFilter]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("YoutubeDL:AutoUpdate:Enabled", true))
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("yt-dlp auto-update skipped — YoutubeDL:AutoUpdate:Enabled is false");
            }
            return;
        }

        // The recurring schedule is the only trigger that doesn't go through
        // YtDlpMaintenanceService (which starts this for manual triggers before enqueueing), so
        // it owns starting/completing the shared in-memory status here.
        if (!maintenanceState.TryStart(YtDlpMaintenanceOperation.Update))
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("yt-dlp auto-update skipped — an update or rollback is already in progress");
            }
            return;
        }

        await RunUpdateAsync(cancellationToken);
    }

    /// <summary>
    /// Admin-triggered entry point (Admin → Tools → yt-dlp → Update). Runs even when
    /// <c>YoutubeDL:AutoUpdate:Enabled</c> is false — the admin explicitly asked for it. The
    /// caller (<see cref="MyVideoArchive.Services.YtDlpMaintenanceService"/>) has already marked
    /// the operation as started before enqueueing this job.
    /// </summary>
    public Task ExecuteManualUpdateAsync(CancellationToken cancellationToken = default)
    {
        maintenanceState.EnsureStarted(YtDlpMaintenanceOperation.Update);
        return RunUpdateAsync(cancellationToken);
    }

    /// <summary>
    /// Admin-triggered entry point (Admin → Tools → yt-dlp → Roll back). Restores yt-dlp from the
    /// backup taken before the last update. The caller has already marked the operation as
    /// started before enqueueing this job.
    /// </summary>
    public async Task ExecuteRollbackAsync(CancellationToken cancellationToken = default)
    {
        maintenanceState.EnsureStarted(YtDlpMaintenanceOperation.Rollback);

        string ytDlpPath = ytdl.YoutubeDLPath;

        if (string.IsNullOrWhiteSpace(ytDlpPath))
        {
            Complete(YtDlpMaintenanceOperation.Rollback, false, "yt-dlp path is not configured.", null, null);
            return;
        }

        string? versionBefore = await TryGetVersionAsync(ytDlpPath, cancellationToken);

        var backup = await backupManager.TryReadMetadataAsync(cancellationToken);
        if (backup is null)
        {
            Complete(YtDlpMaintenanceOperation.Rollback, false, "No backup is available to roll back to. Run an update first.", versionBefore, versionBefore);
            return;
        }

        bool success;
        string message;

        try
        {
            if (YtDlpBackupManager.IsFileBasedMethod(backup.Method))
            {
                success = backupManager.RestoreBackupFile(ytDlpPath);
                message = success
                    ? $"Restored yt-dlp {backup.Version ?? "(unknown version)"} from backup taken {backup.BackedUpAtUtc:u}."
                    : "The backed-up yt-dlp executable was not found on disk.";
            }
            else if (string.IsNullOrWhiteSpace(backup.Version))
            {
                success = false;
                message = "The backup does not record a version to reinstall.";
            }
            else
            {
                string pipExecutable = configuration["YoutubeDL:AutoUpdate:PipExecutable"] ?? "pip3";
                string pipArguments = configuration["YoutubeDL:AutoUpdate:PipArguments"]
                    ?? "install --break-system-packages --no-cache-dir --upgrade";
                // Strip any extras (e.g. "[default]") for the version pin — pip rejects
                // "package[extra]==version" combined with a bare package name backup didn't record extras for.
                string pipPackageName = (backup.PipPackage ?? "yt-dlp[default]").Split('[')[0];
                string pinnedSpec = $"{pipPackageName}=={backup.Version}";

                var result = await RunProcessAsync(pipExecutable, $"{pipArguments} {pinnedSpec}", cancellationToken);
                LogProcessOutput(logger, $"{pipExecutable} {pipArguments} {pinnedSpec}", result);

                success = result.ExitCode == 0;
                message = success
                    ? $"Reinstalled yt-dlp {backup.Version} via pip (backup taken {backup.BackedUpAtUtc:u})."
                    : "pip failed to reinstall the backed-up version. Check the server logs for details.";
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "yt-dlp rollback failed");
            }
            success = false;
            message = "An unexpected error occurred while rolling back. Check the server logs for details.";
        }

        string? versionAfter = success ? await TryGetVersionAsync(ytDlpPath, cancellationToken) : versionBefore;

        if (logger.IsEnabled(success ? LogLevel.Information : LogLevel.Warning))
        {
            logger.Log(success ? LogLevel.Information : LogLevel.Warning,
                "yt-dlp rollback {Outcome}: {Message}", success ? "succeeded" : "failed", message);
        }

        Complete(YtDlpMaintenanceOperation.Rollback, success, message, versionBefore, versionAfter);
    }

    private async Task RunUpdateAsync(CancellationToken cancellationToken)
    {
        string ytDlpPath = ytdl.YoutubeDLPath;
        if (string.IsNullOrWhiteSpace(ytDlpPath))
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("yt-dlp update skipped — yt-dlp path is not configured");
            }
            Complete(YtDlpMaintenanceOperation.Update, false, "yt-dlp path is not configured.", null, null);
            return;
        }

        string method = ResolveUpdateMethod();
        string? versionBefore = await TryGetVersionAsync(ytDlpPath, cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Starting yt-dlp update (method: {Method}, current version: {Version})",
                method, versionBefore ?? "unknown");
        }

        try
        {
            await backupManager.BackupCurrentAsync(
                ytDlpPath, method, versionBefore, ResolvePipPackage(), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Failed to back up yt-dlp before updating; aborting update so rollback stays possible");
            }
            Complete(YtDlpMaintenanceOperation.Update, false, "Failed to back up the current yt-dlp install; the update was not attempted.", versionBefore, versionBefore);
            return;
        }

        bool ran;
        try
        {
            ran = method switch
            {
                "pip" => await UpdateViaPipAsync(cancellationToken),
                "binary" => await UpdateViaBinaryDownloadAsync(ytDlpPath, cancellationToken),
                _ => await UpdateViaSelfAsync(ytDlpPath, cancellationToken),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "yt-dlp update failed (method: {Method})", method);
            }
            Complete(YtDlpMaintenanceOperation.Update, false, "An unexpected error occurred while updating. Check the server logs for details.", versionBefore, versionBefore);
            return;
        }

        if (!ran)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("yt-dlp update did not complete successfully (method: {Method})", method);
            }
            Complete(YtDlpMaintenanceOperation.Update, false, "The update command did not complete successfully. Check the server logs for details.", versionBefore, versionBefore);
            return;
        }

        string? versionAfter = await TryGetVersionAsync(ytDlpPath, cancellationToken);
        string message;
        if (!string.IsNullOrEmpty(versionAfter) &&
            string.Equals(versionBefore, versionAfter, StringComparison.OrdinalIgnoreCase))
        {
            message = $"yt-dlp is already up to date (version {versionAfter}).";
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("{Message}", message);
            }
        }
        else
        {
            message = $"Updated yt-dlp from {versionBefore ?? "unknown"} to {versionAfter ?? "unknown"}.";
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("{Message}", message);
            }
        }

        Complete(YtDlpMaintenanceOperation.Update, true, message, versionBefore, versionAfter);
    }

    private void Complete(
        YtDlpMaintenanceOperation operation, bool success, string message, string? versionBefore, string? versionAfter)
    {
        maintenanceState.Complete(new YtDlpMaintenanceResult(
            operation, success, message, versionBefore, versionAfter, DateTime.UtcNow));
    }

    /// <summary>
    /// Resolves how yt-dlp should be updated. When explicitly configured
    /// (<c>YoutubeDL:AutoUpdate:Method</c> = self | pip | binary) that wins. Otherwise the
    /// method is inferred: a configured <c>YoutubeDL:ExecutablePath</c> means the binary is
    /// provided by the image (Docker installs yt-dlp via pip), so we update through pip.
    /// A missing path means the standalone binary YoutubeDLSharp downloaded on first run,
    /// which supports in-place self-update via <c>-U</c>.
    /// </summary>
    private string ResolveUpdateMethod()
    {
        string? configured = configuration["YoutubeDL:AutoUpdate:Method"];
        if (!string.IsNullOrWhiteSpace(configured) &&
            !configured.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            return configured.Trim().ToLowerInvariant();
        }

        string? execPath = configuration["YoutubeDL:ExecutablePath"];
        return string.IsNullOrWhiteSpace(execPath) ? "self" : "pip";
    }

    private string ResolvePipPackage() => configuration["YoutubeDL:AutoUpdate:PipPackage"] ?? "yt-dlp[default]";

    /// <summary>
    /// Runs yt-dlp's built-in self-updater (<c>yt-dlp -U</c>). Works for the standalone
    /// binaries used in local/desktop installs.
    /// </summary>
    private async Task<bool> UpdateViaSelfAsync(string ytDlpPath, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(ytDlpPath, "-U", cancellationToken);
        LogProcessOutput(logger, "yt-dlp -U", result);
        return result.ExitCode == 0;
    }

    /// <summary>
    /// Re-downloads the latest standalone yt-dlp binary next to the current one via
    /// YoutubeDLSharp's downloader.
    /// </summary>
    private async Task<bool> UpdateViaBinaryDownloadAsync(string ytDlpPath, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(ytDlpPath);
        await Utils.DownloadYtDlp(directory ?? string.Empty).WaitAsync(cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Downloaded latest yt-dlp binary to {Path}", ytDlpPath);
        }

        return File.Exists(ytDlpPath);
    }

    /// <summary>
    /// Upgrades yt-dlp through pip. Used for container images where yt-dlp is installed as a
    /// Python package (see Dockerfile). Note: in an ephemeral container the upgrade lives only
    /// until the container is recreated, at which point the image's pinned version is restored —
    /// the recurring job simply re-applies the upgrade on its next run. The backup metadata (see
    /// <see cref="YtDlpBackupManager"/>) lives under the persisted downloads volume, so a rollback
    /// survives container recreation even though the pip-installed binary itself does not.
    /// </summary>
    private async Task<bool> UpdateViaPipAsync(CancellationToken cancellationToken)
    {
        string pipExecutable = configuration["YoutubeDL:AutoUpdate:PipExecutable"] ?? "pip3";
        string pipArguments = configuration["YoutubeDL:AutoUpdate:PipArguments"]
            ?? "install --break-system-packages --no-cache-dir --upgrade";
        string pipPackage = ResolvePipPackage();

        var result = await RunProcessAsync(pipExecutable, $"{pipArguments} {pipPackage}", cancellationToken);
        LogProcessOutput(logger, $"{pipExecutable} {pipArguments} {pipPackage}", result);
        return result.ExitCode == 0;
    }
}
