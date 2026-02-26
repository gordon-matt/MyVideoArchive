namespace MyVideoArchive.Services;

/// <summary>
/// Downloads thumbnails from external URLs, saves them to disk for archival, and returns a
/// base64 data URL suitable for storing in the database.
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
}