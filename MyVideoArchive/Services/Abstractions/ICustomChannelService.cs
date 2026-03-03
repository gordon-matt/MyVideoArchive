using Ardalis.Result;
using MyVideoArchive.Models.Api;

namespace MyVideoArchive.Services;

public interface ICustomChannelService
{
    Task<Result<CreateChannelResponse>> CreateChannelAsync(CreateCustomChannelRequest request);

    Task<Result> UpdateChannelAsync(int channelId, UpdateCustomChannelRequest request);

    Task<Result<ThumbnailFileInfo>> GetChannelThumbnailAsync(int channelId);

    Task<Result<string>> UploadChannelThumbnailAsync(int channelId, Stream fileStream, string fileName);

    Task<Result<CreateChannelPlaylistResponse>> CreatePlaylistAsync(int channelId, CreateCustomChannelPlaylistRequest request);

    Task<Result> UpdatePlaylistAsync(int playlistId, UpdateCustomChannelPlaylistRequest request);

    Task<Result> DeletePlaylistAsync(int playlistId);

    Task<Result<ThumbnailFileInfo>> GetPlaylistThumbnailAsync(int playlistId);

    Task<Result<string>> UploadPlaylistThumbnailAsync(int playlistId, Stream fileStream, string fileName);

    Task<Result<GetChannelPlaylistsResponse>> GetChannelPlaylistsAsync(int channelId);

    Task<Result<GetVideoPlaylistIdsResponse>> GetVideoPlaylistIdsAsync(int videoId);

    Task<Result> UpdateVideoAsync(int videoId, UpdateCustomVideoRequest request);

    Task<Result<ThumbnailFileInfo>> GetVideoThumbnailAsync(int videoId);

    Task<Result<string>> UploadVideoThumbnailAsync(int videoId, Stream fileStream, string fileName);
}

public record CreateChannelResponse(int Id, string Name, string Platform);
public record CreateChannelPlaylistResponse(int Id, string Name);
public record GetChannelPlaylistsResponse(IReadOnlyList<ChannelPlaylistItem> Playlists);
public record ChannelPlaylistItem(int Id, string Name);
public record GetVideoPlaylistIdsResponse(IReadOnlyList<int> PlaylistIds);