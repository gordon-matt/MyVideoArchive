namespace MyVideoArchive.Models.Responses;

/// <summary>
/// Archive-wide statistics shown on the admin dashboard (home page for administrators).
/// </summary>
public record AdminDashboardStatsResponse(
    int TotalUsers,
    int TotalChannels,
    int TotalVideosDownloaded,
    int TotalVideosAvailable,
    long TotalStorageBytes,
    double TotalDurationHours,
    int TotalPlaylists,
    int TotalCustomPlaylists,
    int TotalSeries,
    int TotalGlobalTags,
    int FailedDownloadsCount,
    int NeedsMetadataReviewCount,
    int QueuedDownloadsCount,
    int VideosDownloadedLast7Days,
    int VideosDownloadedLast30Days,
    IReadOnlyList<ChannelPlatformBreakdown> ChannelsByPlatform,
    IReadOnlyList<TopChannelItem> TopChannelsByVideoCount,
    IReadOnlyList<RecentChannelItem> RecentlyAddedChannels);

public record ChannelPlatformBreakdown(
    string Platform,
    int ChannelCount,
    int VideoCount,
    long StorageBytes);

public record TopChannelItem(
    int Id,
    string Name,
    string? AvatarUrl,
    string? BannerUrl,
    string Platform,
    int VideoCount);

public record RecentChannelItem(
    int Id,
    string Name,
    string? AvatarUrl,
    string Platform,
    DateTime SubscribedAt);
