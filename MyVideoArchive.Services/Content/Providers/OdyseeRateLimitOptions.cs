using YoutubeDLSharp.Options;

namespace MyVideoArchive.Services.Content.Providers;

/// <summary>
/// Shared yt-dlp throttling/identity options for Odysee (LBRY). The LBRY backend rate-limits by
/// IP and blocks are time-based, so the goal is to avoid tripping the limiter in the first place:
/// every yt-dlp call that touches LBRY — downloads AND metadata fetches — must be paced.
///
/// Notes on what actually works (from the yt-dlp lbry extractor source):
/// - <c>--sleep-requests</c> paces the extractor's API/CDN requests; this is the main lever.
/// - A cookies file containing an odysee.com <c>auth_token</c> is forwarded by the extractor as
///   <c>x-lbry-auth-token</c>, authenticating API calls (better rate-limit treatment). Export
///   cookies.txt from a browser session on odysee.com and set <c>CookiesFilePath</c>.
/// - The extractor's streaming-URL checks are non-fatal requests that yt-dlp never retries, so
///   <c>--extractor-retries</c>/<c>--retry-sleep</c> cannot fix a 429 there; the download job's
///   exponential reschedule handles that case instead.
/// - <c>--force-ipv4</c> has been reported to resolve lbry 429s for some networks (opt-in).
///
/// All values are overridable via <c>VideoDownload:Odysee:*</c> configuration keys.
/// </summary>
internal static class OdyseeRateLimitOptions
{
    public static void Apply(OptionSet options, IConfiguration configuration, ILogger? logger = null)
    {
        int sleepRequests = configuration.GetValue<int?>("VideoDownload:Odysee:SleepRequestsSeconds") ?? 2;
        int minSleep = configuration.GetValue<int?>("VideoDownload:Odysee:MinSleepSeconds") ?? 3;
        int maxSleep = configuration.GetValue<int?>("VideoDownload:Odysee:MaxSleepSeconds") ?? 10;
        int extractorRetries = configuration.GetValue<int?>("VideoDownload:Odysee:ExtractorRetries") ?? 5;

        if (sleepRequests > 0)
        {
            options.SleepRequests = sleepRequests;
        }

        if (minSleep > 0)
        {
            options.SleepInterval = minSleep;
            options.MaxSleepInterval = Math.Max(minSleep, maxSleep);
        }

        options.ExtractorRetries = extractorRetries;

        // Exponential backoff (capped) for the retryable HTTP/extractor error paths.
        options.RetrySleep = new[] { "http:exp=1:120", "extractor:exp=1:120" };

        if (configuration.GetValue<bool>("VideoDownload:Odysee:ForceIPv4", false))
        {
            options.ForceIPv4 = true;
        }

        string? cookiesFile = configuration.GetValue<string>("VideoDownload:Odysee:CookiesFilePath");
        if (!string.IsNullOrWhiteSpace(cookiesFile))
        {
            if (File.Exists(cookiesFile))
            {
                options.Cookies = cookiesFile;
            }
            else if (logger?.IsEnabled(LogLevel.Warning) == true)
            {
                logger.LogWarning(
                    "Odysee cookies file configured but not found: {CookiesFilePath}. Continuing without cookies.",
                    cookiesFile);
            }
        }
    }
}
