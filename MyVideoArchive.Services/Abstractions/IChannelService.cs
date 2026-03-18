using Ardalis.Result;
using Extenso.Collections.Generic;
using MyVideoArchive.Models.Requests;
using MyVideoArchive.Models.Responses;

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

    Task<Channel> GetChannelAsync(string platformName, string channelId);

    Task<Result<IPagedCollection<ChannelSubscriberResponse>>> GetChannelSubscribersAsync(int id, CancellationToken cancellationToken = default);

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
    /// Returns true if the channel has never completed a sync (initial sync still pending),
    /// or null if the channel does not exist.
    /// </summary>
    Task<bool?> GetSyncStatusAsync(int channelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all users with a flag indicating whether they are subscribed to the channel.
    /// Admin only.
    /// </summary>
    Task<Result<IReadOnlyList<ChannelUserSubscriptionStatus>>> GetUserSubscriptionsAsync(
        int channelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Trigger sync for all channels
    /// </summary>
    Result SyncAllChannels();

    Result SyncChannel(int channelId);

    /// <summary>
    /// Updates user subscriptions for a channel. Adds and removes UserChannel records
    /// to match the supplied set of subscribed user IDs. Admin only.
    /// </summary>
    Task<Result> UpdateUserSubscriptionsAsync(
        int channelId,
        IEnumerable<string> subscribedUserIds,
        CancellationToken cancellationToken = default);

    Task<bool> UserSubscribedToChannelAsync(int channelId);
}