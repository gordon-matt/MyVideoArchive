using Ardalis.Result;
using MyVideoArchive.Models.Responses;
using MyVideoArchive.Models.Video;

namespace MyVideoArchive.Services;

public interface ITagService
{
    Task<Tag> GetOrCreateTagAsync(string userId, string name);

    Task<Tag> GetStandaloneTagAsync(string userId);

    Task<IEnumerable<int>> GetTagIdsByNameAsync(string userId, IEnumerable<string> tagNames);

    /// <summary>
    /// Get all tag names for the current user (for autocomplete)
    /// </summary>
    Task<Result<GetUserTagsResponse>> GetUserTagsAsync();

    /// <summary>
    /// Get all tags applied to a video for the current user
    /// </summary>
    Task<Result<GetVideoTagsResponse>> GetVideoTagsAsync(int videoId);

    /// <summary>
    /// Asynchronously removes the "standalone" tag from all videos in a channel for a given user.
    /// </summary>
    Task<Result> RemoveStandaloneTagsForChannelAsync(string userId, int channelDbId);

    /// <summary>
    /// Asynchronously removes a specified tag from all videos in a channel for a given user.
    /// </summary>
    Task<Result> RemoveTagsForChannelAsync(string userId, int channelDbId, int tagId);

    /// <summary>
    /// Set the tags for a video (replaces existing tags for this user)
    /// </summary>
    Task<Result> SetVideoTagsAsync(int videoId, SetVideoTagsRequest request);
}