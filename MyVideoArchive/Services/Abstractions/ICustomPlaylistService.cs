using Ardalis.Result;
using MyVideoArchive.Models.Api;

namespace MyVideoArchive.Services;

public interface ICustomPlaylistService
{
    Task<Result> AddVideoToPlaylistAsync(int id, int videoId);

    Task<Result<CreatePlaylistResponse>> CreatePlaylistAsync(CreateCustomPlaylistRequest request);

    Task<Result<PreviewPlaylistResponse>> PreviewPlaylistAsync(PreviewPlaylistRequest request, CancellationToken cancellationToken = default);

    Task<Result<ClonePlaylistResponse>> ClonePlaylistAsync(ClonePlaylistRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeletePlaylistAsync(int id);

    Task<Result<GetPlaylistsResponse>> GetPlaylistsAsync(int page = 1, int pageSize = 60);

    Task<Result<GetPlaylistsForVideoResponse>> GetPlaylistsForVideoAsync(int videoId);

    Task<Result<ThumbnailFileInfo>> GetPlaylistThumbnailAsync(int id);

    Task<Result<GetPlaylistVideosResponse>> GetPlaylistVideosAsync(int id, int page = 1, int pageSize = 60);

    Task<Result> RemoveVideoFromPlaylistAsync(int id, int videoId);

    Task<Result<UpdatePlaylistResponse>> UpdatePlaylistAsync(int id, CreateCustomPlaylistRequest request);

    Task<Result<string>> UploadThumbnailAsync(int id, Stream fileStream, string fileName);
}

public record CreatePlaylistResponse(int Id, string Name);
public record PreviewPlaylistResponse(string Name, string? Description, string? ThumbnailUrl, string? Platform, IReadOnlyList<PreviewVideoItem> Videos);
public record PreviewVideoItem(string VideoId, string Title, string? ThumbnailUrl, int? DurationSeconds, string? ChannelName, string? Url, bool IsInLibrary);
public record ClonePlaylistResponse(int Id, string Name, int TotalVideos, int NewVideos, int AlreadyInLibrary);
public record GetPlaylistsResponse(IReadOnlyList<CustomPlaylistSummary> Playlists, int CurrentPage, int PageSize, int TotalCount, int TotalPages);
public record CustomPlaylistSummary(int Id, string Name, string? Description, string? ThumbnailUrl, DateTime CreatedAt, int VideoCount);
public record GetPlaylistsForVideoResponse(IReadOnlyList<PlaylistSummaryItem> Playlists);
public record PlaylistSummaryItem(int Id, string Name);
public record ThumbnailFileInfo(string PhysicalPath, string ContentType);
public record GetPlaylistVideosResponse(PlaylistInfo Playlist, IReadOnlyList<PlaylistVideoEntry> Videos, int CurrentPage, int PageSize, int TotalCount, int TotalPages);
public record PlaylistInfo(int Id, string Name, string? Description, string? ThumbnailUrl);
public record PlaylistVideoEntry(int Order, PlaylistVideoDetail Video);
public record PlaylistVideoDetail(int Id, string Title, string? ThumbnailUrl, TimeSpan? Duration, DateTime? DownloadedAt, string? Platform, string? Url, int? ViewCount, int? LikeCount, DateTime? UploadDate, string? Description, ChannelInfo Channel);
public record UpdatePlaylistResponse(int Id, string Name);