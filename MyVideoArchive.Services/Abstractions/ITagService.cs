using Ardalis.Result;
using MyVideoArchive.Models.Requests.Channel;
using MyVideoArchive.Models.Requests.Playlist;
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

    Task GarbageCollectUserTagsAsync();

    /// <summary>
    /// Get all tags on a channel.
    /// </summary>
    Task<Result<GetChannelTagsResponse>> GetChannelTagsAsync(int channelId);

    /// <summary>
    /// Get all tags on a custom playlist (my playlist).
    /// </summary>
    Task<Result<GetPlaylistTagsResponse>> GetCustomPlaylistTagsAsync(int customPlaylistId);

    /// <summary>
    /// Returns all global tags with their usage counts (for the admin Tags tab).
    /// </summary>
    Task<Result<GetGlobalTagsResponse>> GetGlobalTagsAsync();

    /// <summary>
    /// Gets or creates a global tag by name. Used for auto-importing platform tags.
    /// </summary>
    Task<Tag> GetOrCreateGlobalTagAsync(string name);

    Task<Tag> GetOrCreateTagAsync(string userId, string name);

    /// <summary>
    /// Get all tags on a playlist.
    /// </summary>
    Task<Result<GetPlaylistTagsResponse>> GetPlaylistTagsAsync(int playlistId);

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
    /// Import platform-provided tags for a channel as global tags. Does not remove existing tags.
    /// </summary>
    Task ImportChannelTagsAsync(int channelId, IEnumerable<string> tagNames);

    /// <summary>
    /// Import platform-provided tags for a playlist as global tags. Does not remove existing tags.
    /// </summary>
    Task ImportPlaylistTagsAsync(int playlistId, IEnumerable<string> tagNames);

    /// <summary>
    /// Import platform-provided tags for a video as global tags. Does not remove existing tags.
    /// </summary>
    Task ImportVideoTagsAsync(int videoId, IEnumerable<string> tagNames);

    /// <summary>
    /// Asynchronously removes the "standalone" tag from all videos in a channel for a given user.
    /// </summary>
    Task<Result> RemoveStandaloneTagsForChannelAsync(string userId, int channelDbId);

    /// <summary>
    /// Asynchronously removes a specified tag from all videos in a channel for a given user.
    /// </summary>
    Task<Result> RemoveTagsForChannelAsync(string userId, int channelDbId, int tagId);

    /// <summary>
    /// Set the tags for a channel (replaces existing tags).
    /// </summary>
    Task<Result> SetChannelTagsAsync(int channelId, SetChannelTagsRequest request);

    /// <summary>
    /// Set the tags for a custom playlist (replaces existing tags).
    /// </summary>
    Task<Result> SetCustomPlaylistTagsAsync(int customPlaylistId, SetPlaylistTagsRequest request);

    /// <summary>
    /// Set the tags for a playlist (replaces existing tags).
    /// </summary>
    Task<Result> SetPlaylistTagsAsync(int playlistId, SetPlaylistTagsRequest request);

    /// <summary>
    /// Set the tags for a video (replaces existing tags for this user)
    /// </summary>
    Task<Result> SetVideoTagsAsync(int videoId, SetVideoTagsRequest request);
}