namespace MyVideoArchive.Models;

public sealed class FileSystemScanProgress
{
    public int TotalChannels { get; init; }

    public int ProcessedChannels { get; init; }

    public string? CurrentChannelName { get; init; }

    public int NewVideos { get; init; }

    public int UpdatedVideos { get; init; }

    public int FlaggedForReview { get; init; }

    public int MissingFiles { get; init; }

    public int PercentComplete => TotalChannels == 0
        ? 0
        : (int)Math.Round((double)ProcessedChannels / TotalChannels * 100);
}