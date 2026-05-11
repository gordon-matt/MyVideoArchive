using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using MyVideoArchive.Models.Requests.AdditionalContent;
using MyVideoArchive.Models.Responses;

namespace MyVideoArchive.Services;

public interface IAdditionalContentService
{
    Task<Result<IReadOnlyList<AdditionalContentItemDto>>> GetChannelItemsAsync(int channelId);

    Task<Result<IReadOnlyList<AdditionalContentItemDto>>> GetPlaylistItemsAsync(int playlistId);

    Task<Result<AdditionalContentItemDto>> UploadAsync(int channelId, IFormFile file, int? playlistId);

    Task<Result> UpdateAsync(int id, UpdateAdditionalContentRequest request);

    Task<Result> DeleteAsync(int id);

    Task<Result<AdditionalContentDownloadInfo>> GetDownloadInfoAsync(int id);
}
