namespace MyVideoArchive.Models.Responses;

public record ChannelPlaylistItem(int Id, string Name);

public record CreateChannelPlaylistResponse(int Id, string Name);

public record CreateChannelResponse(int Id, string Name, string Platform);

public record GetChannelPlaylistsResponse(IReadOnlyList<ChannelPlaylistItem> Playlists);

public record GetVideoPlaylistIdsResponse(IReadOnlyList<int> PlaylistIds);