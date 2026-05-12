using Xabe.FFmpeg;

namespace MyVideoArchive.Services.Content;

/// <summary>
/// Downloads thumbnails from external URLs, saves them to disk, and returns a
/// relative /archive/… URL suitable for storing in the database.
/// Also supports generating thumbnails from locally downloaded video files via ffmpeg.
/// </summary>
public class ThumbnailService
{
    public const string ArchiveUrlPrefix = "/archive/";

    private static readonly Dictionary<string, string> ContentTypeExtensions = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/jpg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif",
    };

    private readonly ILogger<ThumbnailService> logger;
    private readonly IHttpClientFactory httpClientFactory;

    public ThumbnailService(ILogger<ThumbnailService> logger, IHttpClientFactory httpClientFactory)
    {
        this.logger = logger;
        this.httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Returns true if the given URL refers to a local archived file
    /// (i.e. already downloaded and stored under /archive/).
    /// </summary>
    public static bool IsLocalUrl(string? url) =>
        url?.StartsWith(ArchiveUrlPrefix, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Returns true if the given URL is an HTTP/HTTPS URL (a remote thumbnail, not yet saved locally).
    /// </summary>
    public static bool IsRemoteUrl(string? url) =>
        url?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Builds a relative URL from a physical file path and the downloads base path.
    /// E.g. downloadBasePath="C:/Downloads", filePath="C:/Downloads/UCxxx/video.jpg" → "/archive/UCxxx/video.jpg"
    /// </summary>
    /// <remarks>
    /// Each path segment is URI-encoded so characters such as <c>#</c>, spaces, and <c>+</c> work in HTML
    /// <c>src</c> attributes (otherwise <c>#</c> starts a fragment and the rest of the path is never requested).
    /// </remarks>
    public static string BuildRelativeUrl(string downloadBasePath, string filePath)
    {
        string relative = Path.GetRelativePath(downloadBasePath, filePath);
        string normalized = relative.Replace('\\', '/');
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return ArchiveUrlPrefix.TrimEnd('/');
        }

        IEnumerable<string> encoded = segments.Select(static segment =>
        {
            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(segment);
            }
            catch (UriFormatException)
            {
                decoded = segment;
            }

            return Uri.EscapeDataString(decoded);
        });

        return ArchiveUrlPrefix + string.Join("/", encoded);
    }

    /// <summary>
    /// Downloads the thumbnail at <paramref name="thumbnailUrl"/>, writes it to
    /// <c><paramref name="saveDirectory"/>/<paramref name="fileNameWithoutExtension"/>.<em>ext</em></c>
    /// on disk, and returns a <c>/archive/…</c> relative URL for storing in the database.
    /// </summary>
    /// <returns>
    /// A relative <c>/archive/…</c> URL on success; <c>null</c> if the URL is empty,
    /// already a local archive URL, or the download fails.
    /// </returns>
    public async Task<string?> DownloadAndSaveAsync(
        string? thumbnailUrl,
        string saveDirectory,
        string fileNameWithoutExtension,
        string downloadBasePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(thumbnailUrl))
        {
            return null;
        }

        // Already stored as a local archive URL — nothing to re-download
        if (IsLocalUrl(thumbnailUrl))
        {
            return thumbnailUrl;
        }

        if (!IsRemoteUrl(thumbnailUrl))
        {
            return null;
        }

        try
        {
            var httpClient = httpClientFactory.CreateClient("thumbnails");
            using var response = await httpClient.GetAsync(thumbnailUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            string contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            string ext = ContentTypeExtensions.GetValueOrDefault(contentType, ".jpg");

            Directory.CreateDirectory(saveDirectory);
            string filePath = Path.Combine(saveDirectory, fileNameWithoutExtension + ext);
            await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);

            return BuildRelativeUrl(downloadBasePath, filePath);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(ex, "Failed to download thumbnail from {Url}", thumbnailUrl);
            }

            return null;
        }
    }

    /// <summary>
    /// Generates a thumbnail from a locally downloaded video file using ffmpeg, writes the
    /// resulting JPEG to <c><paramref name="saveDirectory"/>/<paramref name="fileNameWithoutExtension"/>.jpg</c>
    /// on disk, and returns a <c>/archive/…</c> relative URL for storing in the database.
    /// </summary>
    /// <returns>
    /// A relative <c>/archive/…</c> URL on success, or <c>null</c> if the file does not exist or
    /// ffmpeg fails.
    /// </returns>
    /// <remarks>
    /// Requires startup configuration via <see cref="FfmpegToolsBootstrapper.ConfigureXabeAsync"/> so that
    /// Xabe can find ffmpeg and ffprobe (see Xabe.FFmpeg <c>ValidateExecutables</c>).
    /// </remarks>
    public async Task<string?> GenerateFromVideoAsync(
        string videoFilePath,
        string saveDirectory,
        string fileNameWithoutExtension,
        string downloadBasePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(videoFilePath))
        {
            return null;
        }

        string outPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jpg");

        try
        {
            var conversion = await FFmpeg.Conversions.FromSnippet.Snapshot(
                videoFilePath, outPath, TimeSpan.FromSeconds(3));

            conversion.SetOverwriteOutput(true);

            cancellationToken.ThrowIfCancellationRequested();
            await conversion.Start();
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(outPath))
            {
                return null;
            }

            byte[] bytes = await File.ReadAllBytesAsync(outPath, cancellationToken);

            Directory.CreateDirectory(saveDirectory);
            string savePath = Path.Combine(saveDirectory, fileNameWithoutExtension + ".jpg");
            await File.WriteAllBytesAsync(savePath, bytes, cancellationToken);

            return BuildRelativeUrl(downloadBasePath, savePath);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(ex, "Failed to generate thumbnail from video file {FilePath}", videoFilePath);
            }

            return null;
        }
        finally
        {
            try
            {
                if (File.Exists(outPath))
                {
                    File.Delete(outPath);
                }
            }
            catch
            {
                // Ignore temp file cleanup failures
            }
        }
    }
}
