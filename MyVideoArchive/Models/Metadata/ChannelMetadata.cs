namespace MyVideoArchive.Models.Metadata;

public record ThumbnailInfo(string? Id, string Url, int? Width, int? Height, int? Preference);

public class ChannelMetadata
{
    public required string ChannelId { get; set; }

    public required string Name { get; set; }

    public required string Url { get; set; }

    public string? Description { get; set; }

    public string? BannerUrl { get; set; }

    public string? AvatarUrl { get; set; }

    public List<ThumbnailInfo> Thumbnails { get; set; } = [];

    public int? SubscriberCount { get; set; }

    public required string Platform { get; set; }
}