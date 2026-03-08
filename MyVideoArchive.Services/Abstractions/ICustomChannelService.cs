using Ardalis.Result;
using MyVideoArchive.Models.Requests.Channel;
using MyVideoArchive.Models.Responses;

namespace MyVideoArchive.Services;

public interface ICustomChannelService
{
    Task<Result<CreateChannelResponse>> CreateChannelAsync(CreateCustomChannelRequest request);

    Task<Result<CreateChannelPlaylistResponse>> CreatePlaylistAsync(int channelId, CreateCustomChannelPlaylistRequest request);

    Task<Result> DeletePlaylistAsync(int playlistId);

    Task<Result<GetChannelPlaylistsResponse>> GetChannelPlaylistsAsync(int channelId);

    Task<Result<ThumbnailFileInfo>> GetChannelThumbnailAsync(int channelId);

    Task<Result<ThumbnailFileInfo>> GetPlaylistThumbnailAsync(int playlistId);

    Task<Result<GetVideoPlaylistIdsResponse>> GetVideoPlaylistIdsAsync(int videoId);

    Task<Result<ThumbnailFileInfo>> GetVideoThumbnailAsync(int videoId);

    Task<Result> UpdateChannelAsync(int channelId, UpdateCustomChannelRequest request);

    Task<Result> UpdatePlaylistAsync(int playlistId, UpdateCustomChannelPlaylistRequest request);

    Task<Result> UpdateVideoAsync(int videoId, UpdateCustomVideoRequest request);

    Task<Result<string>> UploadChannelThumbnailAsync(int channelId, Stream fileStream, string fileName);

    Task<Result<string>> UploadPlaylistThumbnailAsync(int playlistId, Stream fileStream, string fileName);

    Task<Result<string>> UploadVideoThumbnailAsync(int videoId, Stream fileStream, string fileName);
}