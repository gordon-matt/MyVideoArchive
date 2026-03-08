using Ardalis.Result;
using MyVideoArchive.Models.Responses;
using MyVideoArchive.Models.Video;

namespace MyVideoArchive.Services;

public interface ITagService
{
    /// <summary>
    /// Creates a global tag visible to all users as a suggestion.
    /// Any existing per-user tags with the same name are consolidated into this global tag.
    /// </summary>
    Task<Result<GlobalTagItem>> CreateGlobalTagAsync(string name);

    /// <summary>
    /// Deletes a global tag and all VideoTag associations that reference it.
    /// </summary>
    Task<Result> DeleteGlobalTagAsync(int tagId);

    /// <summary>
    /// Returns all global tags with their usage counts (for the admin Tags tab).
    /// </summary>
    Task<Result<GetGlobalTagsResponse>> GetGlobalTagsAsync();

    Task<Tag> GetOrCreateTagAsync(string userId, string name);

    Task<Tag> GetStandaloneTagAsync(string userId);

    Task<IEnumerable<int>> GetTagIdsByNameAsync(string userId, IEnumerable<string> tagNames);

    /// <summary>
    /// Get all tag names for the current user, including global tags (for autocomplete)
    /// </summary>
    Task<Result<GetUserTagsResponse>> GetUserTagsAsync();

    /// <summary>
    /// Get all tags applied to a video for the current user (includes global tags)
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