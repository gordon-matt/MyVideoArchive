namespace MyVideoArchive.Models.Responses;

public record AddStandaloneVideoResponse(int VideoId, string Title, int ChannelId, string ChannelName, bool IsAlreadyDownloaded);

public record ChannelFilterItem(int Id, string Name);

public record GetAccessibleChannelsResponse(IReadOnlyList<ChannelFilterItem> Channels);

public record GetVideoPlaylistsResponse(IReadOnlyList<VideoPlaylistItem> Playlists);

public record GetWatchedVideoIdsResponse(IReadOnlyList<int> WatchedIds);

public record RetryMetadataResponse(bool Success, string Message);

public record StandaloneInfoResponse(
    bool IsStandalone,
    int ChannelVideoCount,
    int ChannelId,
    string ChannelName,
    string? ChannelUrl,
    string? ChannelPlatformId,
    string? ChannelPlatform,
    bool IsSubscribed);

public record VideoIndexItem(
    int Id,
    string VideoId,
    string Title,
    string? ThumbnailUrl,
    TimeSpan? Duration,
    DateTime? UploadDate,
    DateTime? DownloadedAt,
    bool IsQueued,
    string? Platform,
    ChannelInfo Channel,
    IReadOnlyList<string> Tags);

public record VideoIndexPageResponse(
    IReadOnlyList<VideoIndexItem> Videos,
    int CurrentPage,
    int PageSize,
    int TotalCount,
    int TotalPages);

public record VideoPlaylistItem(int Id, string Name, string? Platform, string? Url);

public record VideoStreamInfo(string FilePath, string ContentType);