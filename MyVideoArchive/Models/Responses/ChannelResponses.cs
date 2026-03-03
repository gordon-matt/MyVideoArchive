namespace MyVideoArchive.Models.Responses;

public record AvailableVideo(
    int Id,
    string VideoId,
    string Title,
    string? Description,
    string Url,
    string? ThumbnailUrl,
    TimeSpan? Duration,
    DateTime? UploadDate,
    int? ViewCount,
    int? LikeCount,
    DateTime? DownloadedAt,
    bool IsIgnored,
    bool IsDownloaded,
    bool DownloadFailed);

public record DownloadedVideo(
    int Id,
    string VideoId,
    string Title,
    string Url,
    string? ThumbnailUrl,
    TimeSpan? Duration,
    DateTime? UploadDate,
    DateTime? DownloadedAt);