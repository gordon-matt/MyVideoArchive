using Xabe.FFmpeg;

namespace MyVideoArchive.Services.Content;

/// <summary>
/// Downloads thumbnails from external URLs, saves them to disk for archival, and returns a
/// base64 data URL suitable for storing in the database.
/// Also supports generating thumbnails from locally downloaded video files via ffmpeg.
/// </summary>
public class ThumbnailService
{
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
    /// Downloads the thumbnail at <paramref name="thumbnailUrl"/>, writes it to
    /// <c><paramref name="saveDirectory"/>/<paramref name="fileNameWithoutExtension"/>.<em>ext</em></c>
    /// for archival, and returns a base64 data URL for storing in the database.
    /// </summary>
    /// <returns>
    /// A <c>data:…;base64,…</c> URL on success; the original value if it is already a data URL;
    /// or <c>null</c> if the URL is empty or the download fails.
    /// </returns>
    public async Task<string?> DownloadAndSaveAsync(
        string? thumbnailUrl,
        string saveDirectory,
        string fileNameWithoutExtension,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(thumbnailUrl))
        {
            return null;
        }

        // Already stored as a data URL — nothing to re-download
        if (thumbnailUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return thumbnailUrl;
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

            string base64Data = Convert.ToBase64String(bytes);
            return $"data:{contentType};base64,{base64Data}";
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
    /// for archival, and returns a <c>data:image/jpeg;base64,…</c> URL for storing in the database.
    /// </summary>
    /// <returns>
    /// A <c>data:…;base64,…</c> URL on success, or <c>null</c> if the file does not exist or
    /// ffmpeg fails.
    /// </returns>
    public async Task<string?> GenerateFromVideoAsync(
        string videoFilePath,
        string saveDirectory,
        string fileNameWithoutExtension,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(videoFilePath))
        {
            return null;
        }

        string outPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jpg");

        try
        {
            IConversion conversion = await FFmpeg.Conversions.FromSnippet.Snapshot(
                videoFilePath, outPath, TimeSpan.FromSeconds(3));

            conversion.SetOverwriteOutput(true);

            // Xabe.FFmpeg.Start() does not accept a CancellationToken; check before and after.
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

            string base64 = Convert.ToBase64String(bytes);
            return $"data:image/jpeg;base64,{base64}";
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