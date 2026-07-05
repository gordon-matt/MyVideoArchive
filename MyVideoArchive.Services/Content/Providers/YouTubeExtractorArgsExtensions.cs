using YoutubeDLSharp.Options;

namespace MyVideoArchive.Services.Content.Providers;

/// <summary>
/// Applies an optional, user-configured set of yt-dlp <c>--extractor-args</c> values to every
/// YouTube request (metadata fetch and download).
///
/// Off by default — only takes effect when <c>YoutubeDL:ExtractorArgs</c> is set in
/// configuration. Intended as a manual escape hatch for YouTube-side extractor issues (e.g.
/// player-client A/B tests that suddenly break "invalid argument" HTTP 400s) without needing a
/// code change and redeploy. See https://github.com/yt-dlp/yt-dlp/wiki/Extractors for syntax,
/// e.g. ["youtube:player_client=default,-ios"].
/// </summary>
internal static class YouTubeExtractorArgsExtensions
{
    public static void ApplyConfiguredExtractorArgs(this OptionSet options, IConfiguration configuration)
    {
        string[]? extractorArgs = configuration.GetSection("YoutubeDL:ExtractorArgs").Get<string[]>();
        if (extractorArgs is { Length: > 0 })
        {
            options.ExtractorArgs = extractorArgs;
        }
    }
}
