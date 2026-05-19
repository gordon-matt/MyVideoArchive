using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using MyVideoArchive.Models.Requests.AdditionalContent;
using MyVideoArchive.Models.Responses;

namespace MyVideoArchive.Services;

public interface IAdditionalContentService
{
    Task<Result<IReadOnlyList<AdditionalContentItemDto>>> GetChannelItemsAsync(int channelId);

    Task<Result<IReadOnlyList<AdditionalContentItemDto>>> GetItemsForVideoAsync(int videoId);

    /// <summary>
    /// Items on the same channel that are either not tied to any playlist, or tied to this playlist.
    /// Used to pick extras to associate with a video on the playlist page.
    /// </summary>
    /// <param name="onlyUnassignedInPlaylist">
    /// When true, excludes items already linked to any video that appears in this playlist.
    /// </param>
    Task<Result<IReadOnlyList<AdditionalContentItemDto>>> GetAvailableItemsForVideoOnPlaylistAsync(
        int playlistId,
        int videoId,
        bool onlyUnassignedInPlaylist = false);

    /// <summary>
    /// Channel-wide items that can be linked to a video (e.g. video details or custom playlist pages).
    /// </summary>
    Task<Result<IReadOnlyList<AdditionalContentItemDto>>> GetAvailableItemsForVideoAsync(
        int videoId,
        bool onlyUnassignedOnChannel = false);

    Task<Result<AdditionalContentItemDto>> UploadAsync(int channelId, IFormFile file, IReadOnlyList<int>? playlistIds);

    Task<Result> UpdateAsync(int id, UpdateAdditionalContentRequest request);

    Task<Result> DeleteAsync(int id);

    /// <summary>
    /// Removes the database row and links only; does not delete the file on disk.
    /// </summary>
    Task<Result> DeleteRecordOnlyAsync(int id);

    Task<Result<AdditionalContentDownloadInfo>> GetDownloadInfoAsync(int id);

    Task<Result> LinkItemsToVideoAsync(int videoId, int playlistId, LinkAdditionalContentToVideoRequest request);

    /// <summary>Links channel additional-content files to a video without a channel-playlist context.</summary>
    Task<Result> LinkItemsToVideoAsync(int videoId, LinkAdditionalContentToVideoRequest request);

    Task<Result> UnlinkItemFromVideoAsync(int videoId, int itemId);

    /// <param name="videoId">When set, links the item to this channel video (and to every playlist that contains the video).</param>
    /// <returns><see langword="true"/> when a new <see cref="AdditionalContentItem"/> was created; <see langword="false"/> when the path was already registered.</returns>
    Task<bool> ImportFileAsync(string filePath, int channelId, int? playlistId, int? videoId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// For an already-imported file, ensures playlist/video links exist (e.g. after scan resolves video id from <c>_extras</c> folder name).
    /// </summary>
    Task EnsureAssociationsForPathAsync(
        string filePath,
        int channelId,
        int? playlistId,
        int? videoId,
        CancellationToken cancellationToken = default);
}
