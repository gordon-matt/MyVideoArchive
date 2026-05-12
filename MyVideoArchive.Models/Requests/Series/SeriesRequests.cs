namespace MyVideoArchive.Models.Requests.Series;

public record CreateSeriesRequest(string Name);

public record UpdateSeriesRequest(string Name);

public record UpdateSeriesPlaylistsRequest(List<int> PlaylistIds);
