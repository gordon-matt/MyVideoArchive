namespace MyVideoArchive.Models.Requests.Playlist;

public class CreateCustomPlaylistRequest
{
    public string? Description { get; set; }

    public string Name { get; set; } = string.Empty;
}