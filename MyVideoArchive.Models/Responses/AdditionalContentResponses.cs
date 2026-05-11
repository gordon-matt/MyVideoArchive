namespace MyVideoArchive.Models.Responses;

public record AdditionalContentItemDto(
    int Id,
    string FileName,
    string? ContentType,
    long FileSize,
    DateTime UploadedAt,
    int ChannelId,
    int? PlaylistId,
    string? PlaylistName,
    int? VideoId,
    string? VideoTitle);

public record AdditionalContentDownloadInfo(string PhysicalPath, string ContentType, string DownloadFileName);
