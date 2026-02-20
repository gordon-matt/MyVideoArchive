using MyVideoArchive.Models.Metadata;

namespace MyVideoArchive.Services.Abstractions;

/// <summary>
/// Interface for extracting metadata from various video platforms
/// </summary>
public interface IVideoMetadataProvider
{
    /// <summary>
    /// Gets the platform name (e.g., "YouTube", "Vimeo", etc.)
    /// </summary>
    string PlatformName { get; }

    /// <summary>
    /// Determines if this provider can handle the given URL
    /// </summary>
    bool CanHandle(string url);

    /// <summary>
    /// Retrieves metadata for a channel
    /// </summary>
    Task<ChannelMetadata?> GetChannelMetadataAsync(string channelUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves metadata for a video
    /// </summary>
    Task<VideoMetadata?> GetVideoMetadataAsync(string videoUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves metadata for a playlist
    /// </summary>
    Task<PlaylistMetadata?> GetPlaylistMetadataAsync(string playlistUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves metadata for all videos in a channel
    /// </summary>
    Task<List<VideoMetadata>> GetChannelVideosAsync(string channelUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves metadata for all videos in a playlist
    /// </summary>
    Task<List<VideoMetadata>> GetPlaylistVideosAsync(string playlistUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all playlists for a channel
    /// </summary>
    Task<List<PlaylistMetadata>> GetChannelPlaylistsAsync(string channelUrl, CancellationToken cancellationToken = default);
}
