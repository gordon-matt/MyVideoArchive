namespace MyVideoArchive.Models.Requests.Series;

public record CreateSeriesRequest(string Name);

public record UpdateSeriesRequest(string Name);

/// <summary>
/// Serializable body for updating which playlists belong to a series and their order.
/// Uses a mutable list for reliable JSON model binding.
/// </summary>
public class UpdateSeriesPlaylistsRequest
{
    public List<int> PlaylistIds { get; set; } = [];
}
