using MyVideoArchive.Models;

namespace MyVideoArchive.Services.Content;

public sealed class FileSystemScanStateService
{
    private readonly object syncLock = new();
    private CancellationTokenSource? cts;

    public string? ErrorMessage { get; private set; }

    public bool IsRunning { get; private set; }

    public FileSystemScanResult? LastResult { get; private set; }

    public FileSystemScanProgress? Progress { get; private set; }

    public void Cancel() => cts?.Cancel();

    public void Complete(FileSystemScanResult result)
    {
        lock (syncLock)
        {
            IsRunning = false;
            LastResult = result;
            cts?.Dispose();
            cts = null;
        }
    }

    public void Fail(string errorMessage)
    {
        lock (syncLock)
        {
            IsRunning = false;
            ErrorMessage = errorMessage;
            cts?.Dispose();
            cts = null;
        }
    }

    public object GetStatus() => new
    {
        isRunning = IsRunning,
        progress = Progress is null ? null : new
        {
            totalChannels = Progress.TotalChannels,
            processedChannels = Progress.ProcessedChannels,
            currentChannelName = Progress.CurrentChannelName,
            newVideos = Progress.NewVideos,
            updatedVideos = Progress.UpdatedVideos,
            flaggedForReview = Progress.FlaggedForReview,
            missingFiles = Progress.MissingFiles,
            percentComplete = Progress.PercentComplete
        },
        lastResult = LastResult is null ? null : new
        {
            newVideos = LastResult.NewVideos,
            updatedVideos = LastResult.UpdatedVideos,
            flaggedForReview = LastResult.FlaggedForReview,
            missingFiles = LastResult.MissingFiles
        },
        errorMessage = ErrorMessage
    };

    public bool TryStart(out CancellationToken cancellationToken)
    {
        lock (syncLock)
        {
            if (IsRunning)
            {
                cancellationToken = CancellationToken.None;
                return false;
            }

            cts = new CancellationTokenSource();
            IsRunning = true;
            Progress = null;
            LastResult = null;
            ErrorMessage = null;
            cancellationToken = cts.Token;
            return true;
        }
    }

    public void UpdateProgress(FileSystemScanProgress progress) => Progress = progress;
}