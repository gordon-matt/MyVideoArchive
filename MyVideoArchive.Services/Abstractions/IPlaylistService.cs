using Ardalis.Result;
using Extenso.Collections.Generic;
using MyVideoArchive.Models.Requests.Playlist;
using MyVideoArchive.Models.Responses;
using Microsoft.AspNetCore.Http;

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
    /// Trigger sync for a single playlist.
    /// </summary>
    Task<Result> SyncPlaylistAsync(int playlistId);

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

    /// <summary>
    /// Imports a playlist by URL into a channel. Used for topic channels that cannot
    /// enumerate playlists automatically. Validates that the playlist belongs to the channel.
    /// </summary>
    Task<Result<SubscribePlaylistsResponse>> ImportPlaylistByUrlAsync(
        int channelId,
        string playlistUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a playlist from a topic channel. Only allowed when the channel is a topic channel.
    /// </summary>
    Task<Result> DeleteChannelPlaylistAsync(int channelId, int playlistId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set a playlist thumbnail (only when none exists).
    /// </summary>
    Task<Result<string>> SetPlaylistThumbnailAsync(int playlistId, IFormFile file, CancellationToken cancellationToken = default);
}