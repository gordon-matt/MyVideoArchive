namespace MyVideoArchive.Models.Responses;

public record AvailablePlaylistItem(
    int Id,
    string PlaylistId,
    string Name,
    string? Description,
    string? Url,
    string? ThumbnailUrl,
    string? Platform,
    int? VideoCount,
    DateTime? SubscribedAt,
    DateTime? LastChecked,
    bool IsIgnored,
    bool IsSubscribed);

public record ChannelInfo(int Id, string Name);

public record GetCustomOrderResponse(IReadOnlyList<VideoOrderItem> VideoOrders);

public record GetOrderSettingResponse(bool HasCustomOrder, bool UseCustomOrder);

public record PlaylistOperationsVideosResponse(IReadOnlyList<PlaylistVideoItem> Videos);

public record PlaylistVideoItem(
    int Id,
    string VideoId,
    string Title,
    string? Description,
    string? Url,
    string? ThumbnailUrl,
    TimeSpan? Duration,
    DateTime? UploadDate,
    int? ViewCount,
    int? LikeCount,
    DateTime? DownloadedAt,
    bool IsIgnored,
    bool IsQueued,
    bool DownloadFailed,
    int ChannelId,
    ChannelInfo Channel,
    bool IsHidden);

public record RefreshPlaylistsResponse(string Message, int TotalCount, int NewCount);

public record SubscribePlaylistsResponse(string Message, int SubscribedCount);

public record ToggleIgnorePlaylistResponse(string Message, bool IsIgnored);

public record VideoOrderItem(int VideoId, int Order);