using Ardalis.Result;
using MyVideoArchive.Models;

namespace MyVideoArchive.Services;

public class FileSystemScanService : IFileSystemScanService
{
    private readonly ILogger<FileSystemScanService> logger;
    private readonly FileSystemScanStateService scanState;
    private readonly IServiceScopeFactory scopeFactory;

    public FileSystemScanService(
        ILogger<FileSystemScanService> logger,
        FileSystemScanStateService scanState,
        IServiceScopeFactory scopeFactory)
    {
        this.logger = logger;
        this.scanState = scanState;
        this.scopeFactory = scopeFactory;
    }

    public Task<Result<ScanStartOutcome>> StartScanAsync()
    {
        if (!scanState.TryStart(out var cancellationToken))
        {
            return Task.FromResult(Result<ScanStartOutcome>.Success(ScanStartOutcome.AlreadyRunning));
        }

        _ = Task.Run(async () =>
        {
            logger.LogInformation("File system scan (all channels) initiated");

            try
            {
                using var scope = scopeFactory.CreateScope();
                var scanJob = scope.ServiceProvider.GetRequiredService<FileSystemScanJob>();
                var progress = new Progress<FileSystemScanProgress>(p => scanState.UpdateProgress(p));
                var result = await scanJob.ExecuteAsync(null, progress, cancellationToken);
                scanState.Complete(result);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("File system scan was cancelled");
                scanState.Complete(new FileSystemScanResult());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during file system scan");
                scanState.Fail("An error occurred during the scan. Check the server logs for details.");
            }
        });

        return Task.FromResult(Result<ScanStartOutcome>.Success(ScanStartOutcome.Started));
    }

    public Task<Result<ScanStartOutcome>> StartChannelScanAsync(int channelId)
    {
        if (!scanState.TryStart(out var cancellationToken))
        {
            return Task.FromResult(Result<ScanStartOutcome>.Success(ScanStartOutcome.AlreadyRunning));
        }

        _ = Task.Run(async () =>
        {
            logger.LogInformation("File system scan for channel {ChannelId} initiated", channelId);

            try
            {
                using var scope = scopeFactory.CreateScope();
                var scanJob = scope.ServiceProvider.GetRequiredService<FileSystemScanJob>();
                var progress = new Progress<FileSystemScanProgress>(p => scanState.UpdateProgress(p));
                var result = await scanJob.ExecuteAsync(channelId, progress, cancellationToken);
                scanState.Complete(result);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("File system scan for channel {ChannelId} was cancelled", channelId);
                scanState.Complete(new FileSystemScanResult());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during file system scan for channel {ChannelId}", channelId);
                scanState.Fail("An error occurred during the scan. Check the server logs for details.");
            }
        });

        return Task.FromResult(Result<ScanStartOutcome>.Success(ScanStartOutcome.Started));
    }

    public object GetStatus() => scanState.GetStatus();

    public Result Cancel()
    {
        scanState.Cancel();
        return Result.Success();
    }
}