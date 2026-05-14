using Ardalis.Result;
using MyVideoArchive.Models;

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
    /// Starts a background file system scan across channels (optionally limited by <paramref name="channelScope"/>).
    /// Returns Started if the scan was started, AlreadyRunning if a scan is in progress.
    /// </summary>
    Task<Result<ScanStartOutcome>> StartScanAsync(FileSystemScanChannelScope channelScope = FileSystemScanChannelScope.All);
}

public enum ScanStartOutcome
{
    Started,
    AlreadyRunning
}