using Ardalis.Result;
using Extenso.Collections.Generic;
using MyVideoArchive.Models.Api;

namespace MyVideoArchive.Services;

public interface IPlaylistService
{
    /// <summary>
    /// Get all playlists for a channel (available, subscribed, and ignored), paginated.
    /// </summary>
    Task<Result<IPagedCollection<AvailablePlaylistItem>>> GetAvailablePlaylistsAsync(
        int channelId,
        bool showIgnored = false,
        int page = 1,
        int pageSize = 24,
        CancellationToken cancellationToken = default);

    Task<Result<GetCustomOrderResponse>> GetCustomOrderAsync(int playlistId);

    Task<Result<GetOrderSettingResponse>> GetOrderSettingAsync(int playlistId);

    Task<Result<PlaylistOperationsVideosResponse>> GetPlaylistVideosAsync(int playlistId, bool useCustomOrder = false, bool showHidden = false);

    /// <summary>
    /// Refresh playlists from the platform for a channel.
    /// </summary>
    Task<Result<RefreshPlaylistsResponse>> RefreshPlaylistsAsync(
        int channelId,
        CancellationToken cancellationToken = default);

    Task<Result> SaveCustomOrderAsync(int playlistId, ReorderVideosRequest request);

    Task<Result> SetVideoHiddenAsync(int playlistId, int videoId, SetVideoHiddenRequest request);

    /// <summary>
    /// Subscribe to all playlists for a channel and queue sync jobs.
    /// </summary>
    Task<Result<SubscribePlaylistsResponse>> SubscribeAllPlaylistsAsync(int channelId);

    /// <summary>
    /// Subscribe to selected playlists and queue sync jobs.
    /// </summary>
    Task<Result<SubscribePlaylistsResponse>> SubscribePlaylistsAsync(
        int channelId,
        SubscribePlaylistsRequest request);

    /// <summary>
    /// Trigger sync for all playlists
    /// </summary>
    Result SyncAllPlaylists();

    /// <summary>
    /// Toggle ignore status for a playlist.
    /// </summary>
    Task<Result<ToggleIgnorePlaylistResponse>> ToggleIgnoreAsync(
        int channelId,
        int playlistId,
        IgnorePlaylistRequest request);
}

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

public record SubscribePlaylistsResponse(string Message, int SubscribedCount);

public record ToggleIgnorePlaylistResponse(string Message, bool IsIgnored);

public record RefreshPlaylistsResponse(string Message, int TotalCount, int NewCount);

public record GetOrderSettingResponse(bool HasCustomOrder, bool UseCustomOrder);

public record GetCustomOrderResponse(IReadOnlyList<VideoOrderItem> VideoOrders);

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
    int ChannelId,
    ChannelInfo Channel,
    bool IsHidden);

public record ChannelInfo(int Id, string Name);