namespace MyVideoArchive.Models.Responses;

public record AdditionalContentItemDto(
    int Id,
    string FileName,
    string? ContentType,
    long FileSize,
    DateTime UploadedAt,
    int ChannelId,
    IReadOnlyList<int> PlaylistIds,
    IReadOnlyList<string> PlaylistNames,
    IReadOnlyList<int> VideoIds,
    /// <summary>Path relative to the channel archive folder (e.g. <c>_extras/MyVideo/readme.pdf</c>), when known.</summary>
    string? RelativePath);

public record AdditionalContentDownloadInfo(string PhysicalPath, string ContentType, string DownloadFileName);
