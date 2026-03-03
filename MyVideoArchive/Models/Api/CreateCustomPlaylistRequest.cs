namespace MyVideoArchive.Models.Api;

public class CreateCustomPlaylistRequest
{
    public string? Description { get; set; }
    public string Name { get; set; } = string.Empty;
}