using Ardalis.Result;

namespace MyVideoArchive.Services;

public interface IFileSystemScanService
{
    /// <summary>
    /// Requests cancellation of the currently running file system scan.
    /// </summary>
    Result Cancel();

    /// <summary>
    /// Returns the current status of any running or recently completed file system scan.
    /// </summary>
    object GetStatus();

    /// <summary>
    /// Starts a background file system scan for a single channel.
    /// </summary>
    Task<Result<ScanStartOutcome>> StartChannelScanAsync(int channelId);

    /// <summary>
    /// Starts a background file system scan across all channels.
    /// Returns Started if the scan was started, AlreadyRunning if a scan is in progress.
    /// </summary>
    Task<Result<ScanStartOutcome>> StartScanAsync();
}

public enum ScanStartOutcome
{
    Started,
    AlreadyRunning
}