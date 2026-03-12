namespace MyVideoArchive.Models.Responses;

public record ChannelTagItem(int Id, string Name);

public record GetGlobalTagsResponse(IReadOnlyList<GlobalTagItem> Tags);

public record GetUserTagsResponse(IReadOnlyList<UserTagItem> Tags);

public record GetVideoTagsResponse(IReadOnlyList<VideoTagItem> Tags);

public record GetChannelTagsResponse(IReadOnlyList<ChannelTagItem> Tags);

public record GetPlaylistTagsResponse(IReadOnlyList<PlaylistTagItem> Tags);

public record GlobalTagItem(int Id, string Name, int UsageCount);

public record PlaylistTagItem(int Id, string Name);

public record UserTagItem(int Id, string Name);

public record VideoTagItem(int Id, string Name);