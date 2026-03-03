namespace MyVideoArchive.Models.Requests.Playlist;

public class SubscribePlaylistsRequest
{
    public List<int> PlaylistIds { get; set; } = [];
}