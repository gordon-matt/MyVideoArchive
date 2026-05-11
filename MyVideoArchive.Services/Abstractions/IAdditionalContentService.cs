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
    Task<Result<IReadOnlyList<AdditionalContentItemDto>>> GetAvailableItemsForVideoOnPlaylistAsync(int playlistId, int videoId);

    Task<Result<AdditionalContentItemDto>> UploadAsync(int channelId, IFormFile file, IReadOnlyList<int>? playlistIds);

    Task<Result> UpdateAsync(int id, UpdateAdditionalContentRequest request);

    Task<Result> DeleteAsync(int id);

    Task<Result<AdditionalContentDownloadInfo>> GetDownloadInfoAsync(int id);

    Task<Result> LinkItemsToVideoAsync(int videoId, int playlistId, LinkAdditionalContentToVideoRequest request);

    Task<Result> UnlinkItemFromVideoAsync(int videoId, int itemId);

    Task ImportFileAsync(string filePath, int channelId, int? playlistId, CancellationToken cancellationToken = default);
}
