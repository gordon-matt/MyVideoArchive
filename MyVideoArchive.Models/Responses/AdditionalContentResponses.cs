namespace MyVideoArchive.Models.Responses;

public record AdditionalContentItemDto(
    int Id,
    string FileName,
    string? ContentType,
    long FileSize,
    DateTime UploadedAt,
    int ChannelId,
    IReadOnlyList<int> PlaylistIds,
    IReadOnlyList<string> PlaylistNames);

public record AdditionalContentDownloadInfo(string PhysicalPath, string ContentType, string DownloadFileName);
