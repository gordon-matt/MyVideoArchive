using System.Linq.Expressions;
using Ardalis.Result;
using Extenso.Collections.Generic;
using MyVideoArchive.Models.Api;

namespace MyVideoArchive.Services;

public interface IChannelService
{
    /// <summary>
    /// Admin-only: unsubscribe all users from a channel, with optional metadata and file deletion.
    /// </summary>
    Task<Result> DeleteChannelAsync(int id, bool deleteMetadata = false, bool deleteFiles = false);

    /// <summary>
    /// Download all available videos for a channel
    /// </summary>
    Task<Result<int>> DownloadAllVideosAsync(int channelId);

    /// <summary>
    /// Download selected videos
    /// </summary>
    Task<Result<int>> DownloadVideosAsync(int channelId, DownloadVideosRequest request);

    /// <summary>
    /// Get available videos for a channel (paginated)
    /// </summary>
    Task<Result<IPagedCollection<AvailableVideo>>> GetAvailableVideosAsync(
        int channelId,
        int page = 1,
        int pageSize = 20,
        bool showIgnored = false,
        string? search = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get downloaded videos for a channel with optional search and playlist filtering (paginated)
    /// </summary>
    Task<Result<IPagedCollection<DownloadedVideo>>> GetDownloadedVideosAsync(
        int channelId,
        int page = 1,
        int pageSize = 24,
        string? search = null,
        int? playlistId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Trigger sync for all channels
    /// </summary>
    Result SyncAllChannels();

    Task<bool> UserSubscribedToChannelAsync(int channelId);
}