using Ardalis.Result;
using MyVideoArchive.Models.Api;

namespace MyVideoArchive.Services;

public interface ITagService
{
    /// <summary>
    /// Get all tags applied to a video for the current user
    /// </summary>
    Task<Result<GetVideoTagsResponse>> GetVideoTagsAsync(int videoId);

    /// <summary>
    /// Set the tags for a video (replaces existing tags for this user)
    /// </summary>
    Task<Result> SetVideoTagsAsync(int videoId, SetVideoTagsRequest request);

    /// <summary>
    /// Get all tag names for the current user (for autocomplete)
    /// </summary>
    Task<Result<GetUserTagsResponse>> GetUserTagsAsync();
}

public record GetVideoTagsResponse(IReadOnlyList<VideoTagItem> Tags);

public record VideoTagItem(int Id, string Name);

public record GetUserTagsResponse(IReadOnlyList<UserTagItem> Tags);

public record UserTagItem(int Id, string Name);