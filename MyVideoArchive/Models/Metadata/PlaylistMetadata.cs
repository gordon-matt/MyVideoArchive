namespace MyVideoArchive.Models.Metadata;

public class PlaylistMetadata
{
    public required string PlaylistId { get; set; }
    
    public required string Name { get; set; }
    
    public required string Url { get; set; }
    
    public string? Description { get; set; }
    
    public string? ThumbnailUrl { get; set; }
    
    public required string ChannelId { get; set; }
    
    public required string ChannelName { get; set; }
    
    public int? VideoCount { get; set; }
    
    public required string Platform { get; set; }
    
    public List<string> VideoIds { get; set; } = [];
}
