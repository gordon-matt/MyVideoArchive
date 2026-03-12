namespace MyVideoArchive.Models.Requests.Playlist;

public class IgnorePlaylistRequest
{
    public bool IsIgnored { get; set; }

    /// <summary>
    /// When true, also ignore all not-yet-downloaded videos linked to this playlist.
    /// Applies only when <see cref="IsIgnored"/> is true.
    /// </summary>
    public bool IgnoreVideos { get; set; }
}