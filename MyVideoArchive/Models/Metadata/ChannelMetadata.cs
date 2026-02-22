namespace MyVideoArchive.Models.Metadata;

public class ChannelMetadata
{
    public required string ChannelId { get; set; }

    public required string Name { get; set; }

    public required string Url { get; set; }

    public string? Description { get; set; }

    public string? ThumbnailUrl { get; set; }

    public int? SubscriberCount { get; set; }

    public required string Platform { get; set; }
}