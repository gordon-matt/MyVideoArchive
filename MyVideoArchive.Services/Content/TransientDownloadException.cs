namespace MyVideoArchive.Services.Content;

/// <summary>
/// Thrown when a download fails for a reason that is expected to be temporary (e.g. HTTP 429
/// "Too Many Requests" rate limiting from the source platform). The download job treats these
/// as retryable and reschedules the video instead of marking it permanently failed.
/// </summary>
public class TransientDownloadException : Exception
{
    public TransientDownloadException(string message)
        : base(message)
    {
    }

    public TransientDownloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Returns <c>true</c> when the given yt-dlp error text indicates a transient, retryable
    /// condition such as rate limiting (HTTP 429) or a temporary server error (HTTP 5xx).
    /// </summary>
    public static bool IsTransientError(string? errorText)
    {
        if (string.IsNullOrWhiteSpace(errorText))
        {
            return false;
        }

        return errorText.Contains("429", StringComparison.Ordinal)
            || errorText.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
            || errorText.Contains("HTTP Error 503", StringComparison.OrdinalIgnoreCase)
            || errorText.Contains("HTTP Error 502", StringComparison.OrdinalIgnoreCase)
            || errorText.Contains("Service Unavailable", StringComparison.OrdinalIgnoreCase)
            || errorText.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase);
    }
}
