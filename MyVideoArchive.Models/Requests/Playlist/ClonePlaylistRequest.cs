namespace MyVideoArchive.Models.Requests.Playlist;

public class ClonePlaylistRequest
{
    public string Url { get; set; } = string.Empty;

    public List<string> SelectedVideoIds { get; set; } = [];
}