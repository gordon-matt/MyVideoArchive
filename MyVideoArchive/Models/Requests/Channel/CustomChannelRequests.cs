namespace MyVideoArchive.Models.Requests.Channel;

public record CreateCustomChannelRequest(string Name, string? Description);

public record UpdateCustomChannelRequest(string Name, string? Description, string? ThumbnailUrl);

public record CreateCustomChannelPlaylistRequest(string Name, string? Description);

public record UpdateCustomChannelPlaylistRequest(string Name, string? Description);

public record UpdateCustomVideoRequest(
    string Title,
    string? Description,
    string? ThumbnailUrl,
    DateTime? UploadDate,
    TimeSpan? Duration,
    string? FilePath,
    List<int>? PlaylistIds);