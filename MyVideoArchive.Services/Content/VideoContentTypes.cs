namespace MyVideoArchive.Services.Content;

/// <summary>
/// MIME types for library video files (streaming and client source hints).
/// </summary>
public static class VideoContentTypes
{
    public static string FromFilePath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return "video/mp4";
        }

        return FromExtension(Path.GetExtension(filePath));
    }

    public static string FromExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "application/octet-stream";
        }

        return extension.ToLowerInvariant() switch
        {
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".mkv" => "video/x-matroska",
            ".avi" => "video/x-msvideo",
            ".mov" => "video/quicktime",
            ".flv" => "video/x-flv",
            ".m4v" => "video/mp4",
            ".wmv" => "video/x-ms-wmv",
            _ => "application/octet-stream"
        };
    }
}
