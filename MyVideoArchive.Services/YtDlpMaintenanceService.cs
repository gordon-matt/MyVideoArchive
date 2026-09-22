using System.Linq.Expressions;
using Ardalis.Result;
using Hangfire;
using YoutubeDLSharp;
using static MyVideoArchive.Services.Content.YtDlpProcessHelper;

namespace MyVideoArchive.Services;

/// <summary>
/// Orchestrates admin-triggered yt-dlp maintenance: reports status for the Admin UI and queues
/// the update/rollback Hangfire job (<see cref="YtDlpUpdateJob"/>), which runs in the "downloads"
/// queue so it never races an in-flight video download.
/// </summary>
public class YtDlpMaintenanceService : IYtDlpMaintenanceService
{
    private readonly ILogger<YtDlpMaintenanceService> logger;
    private readonly IBackgroundJobClient backgroundJobClient;
    private readonly YtDlpMaintenanceStateService state;
    private readonly YtDlpBackupManager backupManager;
    private readonly YoutubeDL ytdl;

    public YtDlpMaintenanceService(
        ILogger<YtDlpMaintenanceService> logger,
        IBackgroundJobClient backgroundJobClient,
        YtDlpMaintenanceStateService state,
        YtDlpBackupManager backupManager,
        YoutubeDL ytdl)
    {
        this.logger = logger;
        this.backgroundJobClient = backgroundJobClient;
        this.state = state;
        this.backupManager = backupManager;
        this.ytdl = ytdl;
    }

    public async Task<object> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        string? currentVersion = string.IsNullOrWhiteSpace(ytdl.YoutubeDLPath)
            ? null
            : await TryGetVersionAsync(ytdl.YoutubeDLPath, cancellationToken);

        var backup = await backupManager.TryReadMetadataAsync(cancellationToken);
        var lastResult = state.LastResult;

        return new
        {
            currentVersion,
            isRunning = state.IsRunning,
            currentOperation = state.CurrentOperation?.ToString(),
            lastResult = lastResult is null ? null : new
            {
                operation = lastResult.Operation.ToString(),
                success = lastResult.Success,
                message = lastResult.Message,
                versionBefore = lastResult.VersionBefore,
                versionAfter = lastResult.VersionAfter,
                completedAtUtc = lastResult.CompletedAtUtc
            },
            backup = backup is null ? null : new
            {
                available = true,
                method = backup.Method,
                version = backup.Version,
                backedUpAtUtc = backup.BackedUpAtUtc
            }
        };
    }

    public Result<YtDlpMaintenanceStartOutcome> RequestUpdate()
        => Enqueue(YtDlpMaintenanceOperation.Update, job => job.ExecuteManualUpdateAsync(CancellationToken.None));

    public Result<YtDlpMaintenanceStartOutcome> RequestRollback()
        => Enqueue(YtDlpMaintenanceOperation.Rollback, job => job.ExecuteRollbackAsync(CancellationToken.None));

    private Result<YtDlpMaintenanceStartOutcome> Enqueue(
        YtDlpMaintenanceOperation operation, Expression<Func<YtDlpUpdateJob, Task>> methodCall)
    {
        if (!state.TryStart(operation))
        {
            return Result<YtDlpMaintenanceStartOutcome>.Success(YtDlpMaintenanceStartOutcome.AlreadyRunning);
        }

        try
        {
            backgroundJobClient.Enqueue(methodCall);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Failed to queue yt-dlp {Operation} job", operation);
            }

            state.Complete(new YtDlpMaintenanceResult(
                operation, false, $"Failed to queue the {operation.ToString().ToLowerInvariant()} job.", null, null, DateTime.UtcNow));

            return Result.Error($"Failed to queue the yt-dlp {operation.ToString().ToLowerInvariant()} job.");
        }

        return Result<YtDlpMaintenanceStartOutcome>.Success(YtDlpMaintenanceStartOutcome.Started);
    }
}
