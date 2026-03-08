using Ardalis.Result;
using MyVideoArchive.Models.Requests.Playlist;
using MyVideoArchive.Models.Responses;

namespace MyVideoArchive.Services;

public interface ICustomPlaylistService
{
    Task<Result> AddVideoToPlaylistAsync(int id, int videoId);

    Task<Result<ClonePlaylistResponse>> ClonePlaylistAsync(ClonePlaylistRequest request, CancellationToken cancellationToken = default);

    Task<Result<CreatePlaylistResponse>> CreatePlaylistAsync(CreateCustomPlaylistRequest request);

    Task<Result> DeletePlaylistAsync(int id);

    Task<Result<GetPlaylistsResponse>> GetPlaylistsAsync(int page = 1, int pageSize = 60);

    Task<Result<GetPlaylistsForVideoResponse>> GetPlaylistsForVideoAsync(int videoId);

    Task<Result<ThumbnailFileInfo>> GetPlaylistThumbnailAsync(int id);

    Task<Result<GetPlaylistVideosResponse>> GetPlaylistVideosAsync(int id, int page = 1, int pageSize = 60);

    Task<Result<PreviewPlaylistResponse>> PreviewPlaylistAsync(PreviewPlaylistRequest request, CancellationToken cancellationToken = default);

    Task<Result> RemoveVideoFromAllPlaylistsAsync(int videoId);

    Task<Result> RemoveVideoFromAllPlaylistsForUserAsync(int videoId, string userId);

    Task<Result> RemoveVideoFromPlaylistAsync(int id, int videoId);

    Task<Result<UpdatePlaylistResponse>> UpdatePlaylistAsync(int id, CreateCustomPlaylistRequest request);

    Task<Result<string>> UploadThumbnailAsync(int id, Stream fileStream, string fileName);

    Task<Result<bool>> VideoAppearsOnAnyPlaylistsForOtherUsers(int videoId, string userId);
}