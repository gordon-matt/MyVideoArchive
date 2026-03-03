using Ardalis.Result;

namespace MyVideoArchive.Services;

public interface IFileSystemScanService
{
    /// <summary>
    /// Starts a background file system scan across all channels.
    /// Returns Started if the scan was started, AlreadyRunning if a scan is in progress.
    /// </summary>
    Task<Result<ScanStartOutcome>> StartScanAsync();

    /// <summary>
    /// Starts a background file system scan for a single channel.
    /// </summary>
    Task<Result<ScanStartOutcome>> StartChannelScanAsync(int channelId);

    /// <summary>
    /// Returns the current status of any running or recently completed file system scan.
    /// </summary>
    object GetStatus();

    /// <summary>
    /// Requests cancellation of the currently running file system scan.
    /// </summary>
    Result Cancel();
}

public enum ScanStartOutcome
{
    Started,
    AlreadyRunning
}