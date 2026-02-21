namespace MyVideoArchive.Models.Metadata;

public class VideoMetadata
{
    public required string VideoId { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    public required string Url { get; set; }

    public string? ThumbnailUrl { get; set; }

    public TimeSpan? Duration { get; set; }

    public DateTime? UploadDate { get; set; }

    public int? ViewCount { get; set; }

    public int? LikeCount { get; set; }

    public required string ChannelId { get; set; }

    public required string ChannelName { get; set; }

    public required string Platform { get; set; }

    public string? PlaylistId { get; set; }
}