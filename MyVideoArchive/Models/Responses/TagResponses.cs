namespace MyVideoArchive.Models.Responses;

public record GetUserTagsResponse(IReadOnlyList<UserTagItem> Tags);

public record GetVideoTagsResponse(IReadOnlyList<VideoTagItem> Tags);

public record UserTagItem(int Id, string Name);

public record VideoTagItem(int Id, string Name);