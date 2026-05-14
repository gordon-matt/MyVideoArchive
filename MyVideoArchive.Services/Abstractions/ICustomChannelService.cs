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

    Task<Result<int>> BulkAddVideosToPlaylistAsync(int playlistId, IReadOnlyList<int> videoIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// For custom-channel playlists with no uploaded thumbnail URL, returns the first video's
    /// thumbnail keyed by playlist id (only entries with a non-empty URL).
    /// </summary>
    Task<Result<PlaylistThumbnailFallbacksResponse>> GetPlaylistThumbnailFallbacksAsync(int channelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stored playlist thumbnail URL, or the first linked video's thumbnail for custom playlists when none is set.
    /// </summary>
    Task<Result<string?>> GetPlaylistDisplayThumbnailAsync(int playlistId, CancellationToken cancellationToken = default);
}