namespace MyVideoArchive.Models;

/// <summary>
/// Which channels to include when running a full (non single-channel) file system scan.
/// </summary>
public enum FileSystemScanChannelScope
{
    /// <summary>All registered channels (Custom and non-Custom).</summary>
    All = 0,

    /// <summary>Only non-Custom (YouTube, BitChute, etc.) channels.</summary>
    PlatformChannels = 1,

    /// <summary>Only Custom platform channels under <c>_Custom</c>.</summary>
    CustomChannels = 2
}
