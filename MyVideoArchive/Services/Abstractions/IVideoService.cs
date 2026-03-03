using Ardalis.Result;
using MyVideoArchive.Models.Api;

namespace MyVideoArchive.Services;

public interface IVideoService
{
    /// <summary>
    /// Add a standalone video by URL. Fetches metadata, creates channel if needed, tags as standalone, and queues for download.
    /// </summary>
    Task<Result<AddStandaloneVideoResponse>> AddStandaloneVideoAsync(AddStandaloneVideoRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete the physical file for a downloaded video, clearing download metadata and marking it ignored
    /// </summary>
    Task<Result> DeleteVideoFileAsync(int channelId, int videoId);

    /// <summary>
    /// Get channels accessible to the current user (for the channel filter dropdown)
    /// </summary>
    Task<Result<GetAccessibleChannelsResponse>> GetAccessibleChannelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get standalone status info for a video (for the banner on the details page)
    /// </summary>
    Task<Result<StandaloneInfoResponse>> GetStandaloneInfoAsync(int videoId);

    /// <summary>
    /// Get playlists that contain a specific video
    /// </summary>
    Task<Result<GetVideoPlaylistsResponse>> GetVideoPlaylistsAsync(int videoId);

    /// <summary>
    /// Get paginated videos accessible to the current user, with optional filters
    /// </summary>
    Task<Result<VideoIndexPageResponse>> GetVideosAsync(
        int page = 1,
        int pageSize = 60,
        string? search = null,
        int? channelId = null,
        string? tagFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get file path and content type for streaming a video (caller is responsible for opening the stream)
    /// </summary>
    Task<Result<VideoStreamInfo>> GetVideoStreamInfoAsync(int videoId);

    /// <summary>
    /// Returns the IDs of videos (from the supplied list) that the current user has watched
    /// </summary>
    Task<Result<GetWatchedVideoIdsResponse>> GetWatchedVideoIdsAsync(int[] videoIds);

    /// <summary>
    /// Mark a video as unwatched for the current user
    /// </summary>
    Task<Result> MarkUnwatchedAsync(int videoId);

    /// <summary>
    /// Mark a video as watched for the current user
    /// </summary>
    Task<Result> MarkWatchedAsync(int videoId);

    /// <summary>
    /// Retry fetching platform metadata for a video flagged as needing review.
    /// </summary>
    Task<Result<RetryMetadataResponse>> RetryMetadataAsync(int videoId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Toggle ignore status for a video
    /// </summary>
    Task<Result<bool>> ToggleIgnoreAsync(int channelId, int videoId, IgnoreVideoRequest request);
}

public record RetryMetadataResponse(bool Success, string Message);

public record GetVideoPlaylistsResponse(IReadOnlyList<VideoPlaylistItem> Playlists);

public record VideoPlaylistItem(int Id, string Name, string? Platform, string? Url);

public record VideoStreamInfo(string FilePath, string ContentType);

public record AddStandaloneVideoResponse(int VideoId, string Title, int ChannelId, string ChannelName, bool IsAlreadyDownloaded);

public record StandaloneInfoResponse(
    bool IsStandalone,
    int ChannelVideoCount,
    int ChannelId,
    string ChannelName,
    string? ChannelUrl,
    string? ChannelPlatformId,
    string? ChannelPlatform,
    bool IsSubscribed);

public record VideoIndexPageResponse(
    IReadOnlyList<VideoIndexItem> Videos,
    int CurrentPage,
    int PageSize,
    int TotalCount,
    int TotalPages);

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

public record GetAccessibleChannelsResponse(IReadOnlyList<ChannelFilterItem> Channels);

public record ChannelFilterItem(int Id, string Name);

public record GetWatchedVideoIdsResponse(IReadOnlyList<int> WatchedIds);