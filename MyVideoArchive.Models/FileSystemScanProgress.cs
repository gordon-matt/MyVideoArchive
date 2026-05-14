namespace MyVideoArchive.Models;

public sealed class FileSystemScanProgress
{
    public int TotalChannels { get; init; }

    public int ProcessedChannels { get; init; }

    public string? CurrentChannelName { get; init; }

    /// <summary>
    /// Progress within the channel currently being scanned (e.g. custom: video files enumerated).
    /// </summary>
    public int CurrentChannelWorkProcessed { get; init; }

    /// <summary>
    /// Total units for the current channel phase, or 0 when not applicable.
    /// </summary>
    public int CurrentChannelWorkTotal { get; init; }

    public int NewVideos { get; init; }

    public int UpdatedVideos { get; init; }

    public int FlaggedForReview { get; init; }

    public int MissingFiles { get; init; }

    /// <summary>
    /// 0–100: blends fully completed channels with fractional progress inside the active channel.
    /// </summary>
    public int PercentComplete
    {
        get
        {
            if (TotalChannels <= 0)
            {
                return 0;
            }

            double innerFraction = CurrentChannelWorkTotal > 0
                ? Math.Clamp((double)CurrentChannelWorkProcessed / CurrentChannelWorkTotal, 0d, 1d)
                : 0d;

            double overall = (ProcessedChannels + innerFraction) / TotalChannels;
            return (int)Math.Round(Math.Clamp(overall * 100d, 0d, 100d));
        }
    }
}