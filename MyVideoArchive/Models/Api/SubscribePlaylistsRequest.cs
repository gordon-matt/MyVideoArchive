namespace MyVideoArchive.Models.Api;

public class SubscribePlaylistsRequest
{
    public List<int> PlaylistIds { get; set; } = [];
}