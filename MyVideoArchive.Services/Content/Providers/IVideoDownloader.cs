namespace MyVideoArchive.Services.Content.Providers;

/// <summary>
/// Interface for downloading videos from various platforms
/// </summary>
public interface IVideoDownloader
{
    /// <summary>
    /// Gets the platform name (e.g., "YouTube", "Vimeo", etc.)
    /// </summary>
    string PlatformName { get; }

    /// <summary>
    /// Determines if this downloader can handle the given URL
    /// </summary>
    bool CanHandle(string url);

    /// <summary>
    /// Downloads a video from the given URL to the specified output path
    /// </summary>
    /// <param name="videoUrl">The URL of the video to download</param>
    /// <param name="outputPath">The directory where the video should be saved</param>
    /// <param name="progress">Optional progress callback (0.0 to 1.0)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The full path to the downloaded file</returns>
    Task<string> DownloadVideoAsync(
        string videoUrl,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads only the sidecar subtitle files (.vtt) for an already-downloaded video,
    /// using yt-dlp's <c>--skip-download</c> mode. Honours the <c>Subtitles</c> configuration
    /// section; does nothing when subtitles are globally disabled.
    /// </summary>
    /// <param name="videoUrl">The URL of the video whose subtitles to fetch.</param>
    /// <param name="outputPath">Directory where sidecar files should be written (typically the channel folder).</param>
    /// <returns>True when yt-dlp succeeded (zero or more sub files written), false on error.</returns>
    Task<bool> DownloadSubtitlesAsync(
        string videoUrl,
        string outputPath,
        CancellationToken cancellationToken = default);
}