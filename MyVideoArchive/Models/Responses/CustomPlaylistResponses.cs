namespace MyVideoArchive.Models.Responses;

public record ClonePlaylistResponse(int Id, string Name, int TotalVideos, int NewVideos, int AlreadyInLibrary);

public record CreatePlaylistResponse(int Id, string Name);

public record CustomPlaylistSummary(int Id, string Name, string? Description, string? ThumbnailUrl, DateTime CreatedAt, int VideoCount);

public record GetPlaylistsForVideoResponse(IReadOnlyList<PlaylistSummaryItem> Playlists);

public record GetPlaylistsResponse(
    IReadOnlyList<CustomPlaylistSummary> Playlists, int CurrentPage, int PageSize, int TotalCount, int TotalPages);

public record GetPlaylistVideosResponse(
    PlaylistInfo Playlist, IReadOnlyList<PlaylistVideoEntry> Videos, int CurrentPage, int PageSize, int TotalCount, int TotalPages);

public record PlaylistInfo(int Id, string Name, string? Description, string? ThumbnailUrl);

public record PlaylistSummaryItem(int Id, string Name);

public record PlaylistVideoDetail(
    int Id,
    string Title,
    string? ThumbnailUrl,
    TimeSpan? Duration,
    DateTime? DownloadedAt,
    string? Platform,
    string? Url,
    int? ViewCount,
    int? LikeCount,
    DateTime? UploadDate,
    string? Description,
    ChannelInfo Channel);

public record PlaylistVideoEntry(int Order, PlaylistVideoDetail Video);

public record PreviewPlaylistResponse(
    string Name, string? Description, string? ThumbnailUrl, string? Platform, IReadOnlyList<PreviewVideoItem> Videos);

public record PreviewVideoItem(string VideoId, string Title, string? ThumbnailUrl, int? DurationSeconds, string? ChannelName, string? Url, bool IsInLibrary);

public record ThumbnailFileInfo(string PhysicalPath, string ContentType);

public record UpdatePlaylistResponse(int Id, string Name);