using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MyVideoArchive.Models.Metadata;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
using YoutubeDLSharp.Options;

namespace MyVideoArchive.Services.Content.Providers;

/// <summary>
/// Rumble implementation of metadata provider using yt-dlp.
/// Supports channel URLs (rumble.com/c/{id} and rumble.com/user/{id}) and single video URLs.
///
/// yt-dlp's Rumble channel extractor scrapes the channel page for video links but — unlike
/// YouTube/BitChute — only ever yields bare URLs (no id/title/thumbnail) for flat-playlist
/// listings, because Rumble's channel grid contains nothing but the video link in static HTML.
/// To get usable title/thumbnail/duration for each video without doing a full (slow, one
/// extra HTTP request per video) yt-dlp extraction, we enrich each entry via Rumble's public
/// oEmbed endpoint (https://rumble.com/api/Media/oembed.json), which is lightweight and does
/// not require resolving video formats.
/// </summary>
public partial class RumbleMetadataProvider : IVideoMetadataProvider
{
    private readonly ILogger<RumbleMetadataProvider> logger;
    private readonly YoutubeDL ytdl;
    private readonly HttpClient httpClient;

    public RumbleMetadataProvider(
        ILogger<RumbleMetadataProvider> logger,
        YoutubeDL ytdl,
        IHttpClientFactory httpClientFactory)
    {
        this.logger = logger;
        this.ytdl = ytdl;
        httpClient = httpClientFactory.CreateClient();
    }

    public string PlatformName => "Rumble";

    public string BuildChannelUrl(string channelId) => channelId.StartsWith("http", StringComparison.OrdinalIgnoreCase)
        ? channelId
        : $"https://rumble.com/c/{channelId}";

    public bool CanHandle(string url) => RumbleUrlRegex().IsMatch(url);

    public async Task<ChannelMetadata?> GetChannelMetadataAsync(string channelUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Fetching Rumble channel metadata for: {Url}", channelUrl);
            }

            var options = new OptionSet
            {
                FlatPlaylist = true,
                DumpSingleJson = true,
                PlaylistEnd = 1
            };

            var result = await ytdl.RunVideoDataFetch(channelUrl, overrideOptions: options, ct: cancellationToken);

            if (!result.Success || result.Data is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed to fetch Rumble channel metadata for {Url}: {Error}", channelUrl, string.Join(", ", result.ErrorOutput));
                }

                return null;
            }

            var data = result.Data;

            string channelId = FirstNonEmpty(
                data.ChannelID,
                data.UploaderID,
                data.ID,
                RumbleChannelIdRegex().Match(channelUrl).Groups[1].Value);

            // Rumble's channel extractor does not expose a display name or avatar/banner image,
            // so enrich via oEmbed using the one video entry we asked for (PlaylistEnd = 1).
            var firstEntryUrl = data.Entries?.FirstOrDefault(e => e is not null)?.Url;
            var oembed = string.IsNullOrEmpty(firstEntryUrl) ? null : await GetOEmbedAsync(firstEntryUrl, cancellationToken);

            string name = FirstNonEmpty(oembed?.AuthorName, data.Channel, data.Uploader, data.Title, channelId);
            string? imageUrl = oembed?.ThumbnailUrl;

            return new ChannelMetadata
            {
                ChannelId = channelId,
                Name = string.IsNullOrEmpty(name) ? "Unknown Channel" : name,
                Url = data.WebpageUrl ?? channelUrl,
                Description = data.Description,
                BannerUrl = PickThumbnail(data.Thumbnails, data.Thumbnail ?? imageUrl),
                AvatarUrl = PickThumbnail(data.Thumbnails, data.Thumbnail ?? imageUrl),
                Thumbnails = MapThumbnails(data.Thumbnails, imageUrl),
                SubscriberCount = data.ChannelFollowerCount is not null ? (int?)data.ChannelFollowerCount : null,
                Platform = PlatformName
            };
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error fetching Rumble channel metadata for {Url}", channelUrl);
            }
            return null;
        }
    }

    /// <summary>
    /// yt-dlp has no dedicated extractor for Rumble playlists, so this returns an empty list.
    /// </summary>
    public Task<List<PlaylistMetadata>> GetChannelPlaylistsAsync(string channelUrl, CancellationToken cancellationToken = default)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Rumble does not support channel playlist listing; returning empty for {Url}", channelUrl);
        }

        return Task.FromResult(new List<PlaylistMetadata>());
    }

    public async Task<List<VideoMetadata>> GetChannelVideosAsync(string channelUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Fetching Rumble channel videos for: {Url}", channelUrl);
            }

            var options = new OptionSet
            {
                FlatPlaylist = true,
                DumpSingleJson = true,
                // A transient block/rate-limit on a later page (Rumble's channel pages are
                // fetched one-by-one via ?page=N) would otherwise abort the whole fetch and
                // discard every page already collected; this keeps whatever was gathered.
                IgnoreErrors = true
            };

            var result = await ytdl.RunVideoDataFetch(channelUrl, overrideOptions: options, ct: cancellationToken);

            if (!result.Success || result.Data is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed to fetch Rumble channel videos for {Url}: {Error}", channelUrl, string.Join(", ", result.ErrorOutput));
                }

                return [];
            }

            string channelId = FirstNonEmpty(
                result.Data.ChannelID,
                result.Data.UploaderID,
                result.Data.ID,
                RumbleChannelIdRegex().Match(channelUrl).Groups[1].Value);

            string channelName = FirstNonEmpty(result.Data.Channel, result.Data.Uploader, result.Data.Title, channelId);
            if (string.IsNullOrEmpty(channelName))
            {
                channelName = "Unknown Channel";
            }

            if (logger.IsEnabled(LogLevel.Information))
            {
                int rawCount = result.Data.Entries?.Length ?? 0;
                logger.LogInformation(
                    "Rumble channel page for {Url} yielded {Count} raw entries before enrichment", channelUrl, rawCount);
            }

            return await MapEntriesToVideoMetadataAsync(result.Data.Entries, channelId, channelName, cancellationToken);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error fetching Rumble channel videos for {Url}", channelUrl);
            }

            return [];
        }
    }

    public async Task<PlaylistMetadata?> GetPlaylistMetadataAsync(string playlistUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Fetching Rumble playlist metadata for: {Url}", playlistUrl);
            }

            var options = new OptionSet
            {
                FlatPlaylist = true,
                DumpSingleJson = true
            };

            var result = await ytdl.RunVideoDataFetch(playlistUrl, overrideOptions: options, ct: cancellationToken);

            if (!result.Success || result.Data is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed to fetch Rumble playlist metadata for {Url}: {Error}", playlistUrl, string.Join(", ", result.ErrorOutput));
                }

                return null;
            }

            var data = result.Data;

            return new PlaylistMetadata
            {
                PlaylistId = data.ID ?? string.Empty,
                Name = data.Title ?? "Unknown Playlist",
                Url = data.WebpageUrl ?? playlistUrl,
                ThumbnailUrl = PickThumbnail(data.Thumbnails, data.Thumbnail),
                Description = data.Description,
                ChannelId = data.ChannelID ?? data.UploaderID ?? string.Empty,
                ChannelName = data.Channel ?? data.Uploader ?? "Unknown Channel",
                Platform = PlatformName
            };
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error fetching Rumble playlist metadata for {Url}", playlistUrl);
            }

            return null;
        }
    }

    public async Task<List<VideoMetadata>> GetPlaylistVideosAsync(string playlistUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Fetching Rumble playlist videos for: {Url}", playlistUrl);
            }

            var options = new OptionSet
            {
                FlatPlaylist = true,
                DumpSingleJson = true
            };

            var result = await ytdl.RunVideoDataFetch(playlistUrl, overrideOptions: options, ct: cancellationToken);

            if (!result.Success || result.Data is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed to fetch Rumble playlist videos for {Url}: {Error}", playlistUrl, string.Join(", ", result.ErrorOutput));
                }

                return [];
            }

            string channelId = result.Data.ChannelID ?? result.Data.UploaderID ?? string.Empty;
            string channelName = result.Data.Channel ?? result.Data.Uploader ?? "Unknown Channel";

            return await MapEntriesToVideoMetadataAsync(result.Data.Entries, channelId, channelName, cancellationToken);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error fetching Rumble playlist videos for {Url}", playlistUrl);
            }

            return [];
        }
    }

    public async Task<VideoMetadata?> GetVideoMetadataAsync(string videoUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Fetching Rumble video metadata for: {Url}", videoUrl);
            }

            var result = await ytdl.RunVideoDataFetch(videoUrl, ct: cancellationToken);

            if (!result.Success || result.Data is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed to fetch Rumble video metadata for {Url}: {Error}", videoUrl, string.Join(", ", result.ErrorOutput));
                }

                return null;
            }

            var data = result.Data;

            string videoId = !string.IsNullOrEmpty(data.ID)
                ? data.ID
                : ExtractRumbleVideoId(data.WebpageUrl ?? videoUrl);

            return new VideoMetadata
            {
                VideoId = videoId,
                Title = data.Title ?? "Unknown Title",
                Description = data.Description,
                Url = data.WebpageUrl ?? videoUrl,
                ThumbnailUrl = PickThumbnail(data.Thumbnails, data.Thumbnail),
                Duration = data.Duration.HasValue ? TimeSpan.FromSeconds(data.Duration.Value) : null,
                UploadDate = (data.UploadDate ?? data.Timestamp).AsUtc(),
                ViewCount = data.ViewCount.HasValue ? (int?)data.ViewCount : null,
                LikeCount = data.LikeCount.HasValue ? (int?)data.LikeCount : null,
                ChannelId = data.ChannelID ?? data.UploaderID,
                ChannelName = data.Channel ?? data.Uploader ?? "Unknown Channel",
                Platform = PlatformName,
                PlaylistId = null
            };
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error fetching Rumble video metadata for {Url}", videoUrl);
            }
            return null;
        }
    }

    [GeneratedRegex(@"rumble\.com/(?:c|user)/([^/?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RumbleChannelIdRegex();

    [GeneratedRegex(@"rumble\.com", RegexOptions.IgnoreCase)]
    private static partial Regex RumbleUrlRegex();

    // Rumble video URLs look like https://rumble.com/v6abc12-some-title-slug.html
    // (or /embed/v6abc12/). The native video id is the leading "v…" token.
    [GeneratedRegex(@"rumble\.com/(?:embed/)?(v[0-9a-z]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RumbleVideoIdRegex();

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string ExtractRumbleVideoId(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return string.Empty;
        }

        var match = RumbleVideoIdRegex().Match(url);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    /// <summary>
    /// Builds a readable title from a Rumble video URL slug, used only as a last-resort fallback
    /// when both yt-dlp and the oEmbed lookup fail to provide one. e.g.
    /// ".../v6abc12-some-great-title.html" -> "Some great title".
    /// </summary>
    private static string? DeriveTitleFromRumbleUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        string segment = url.Split('?', '#')[0].TrimEnd('/');
        int lastSlash = segment.LastIndexOf('/');
        if (lastSlash >= 0)
        {
            segment = segment[(lastSlash + 1)..];
        }

        if (segment.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            segment = segment[..^5];
        }

        // Drop the leading "v<id>-" token so only the human-readable slug remains.
        int dash = segment.IndexOf('-');
        if (dash < 0)
        {
            return null;
        }

        string slug = segment[(dash + 1)..].Replace('-', ' ').Trim();
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        return char.ToUpperInvariant(slug[0]) + slug[1..];
    }

    /// <summary>
    /// Calls Rumble's public oEmbed endpoint for a video URL to retrieve its title, channel
    /// (author) name, thumbnail and duration. This is a single lightweight JSON request — unlike
    /// full yt-dlp extraction it does not resolve video formats/streams — used to enrich the
    /// otherwise-empty entries returned by Rumble's flat channel listing.
    /// </summary>
    private async Task<RumbleOEmbedResult?> GetOEmbedAsync(string videoUrl, CancellationToken cancellationToken)
    {
        try
        {
            string requestUrl = $"https://rumble.com/api/Media/oembed.json?url={Uri.EscapeDataString(videoUrl)}";
            using var response = await httpClient.GetAsync(requestUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("Rumble oEmbed lookup failed for {Url}: {StatusCode}", videoUrl, response.StatusCode);
                }
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<RumbleOEmbedResult>(stream, JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(ex, "Error calling Rumble oEmbed for {Url}", videoUrl);
            }
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static string? PickThumbnail(ThumbnailData[]? thumbnails, string? singleThumbnail)
    {
        if (!thumbnails.IsNullOrEmpty())
        {
            var best = thumbnails!
                .Where(t => !string.IsNullOrEmpty(t.Url))
                .OrderByDescending(t => (t.Width ?? 0) * (t.Height ?? 0))
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(best?.Url))
            {
                return best.Url;
            }
        }

        return string.IsNullOrEmpty(singleThumbnail) ? null : singleThumbnail;
    }

    /// <summary>
    /// Builds the thumbnail candidate list shown in the "Select Channel Images" picker.
    /// Rumble's channel-level yt-dlp fetch never populates a <see cref="ThumbnailData"/> array, so
    /// without the oEmbed-derived fallback image the picker would always be empty.
    /// </summary>
    private static List<ThumbnailInfo> MapThumbnails(ThumbnailData[]? thumbnails, string? fallbackUrl)
    {
        var list = thumbnails.IsNullOrEmpty()
            ? []
            : thumbnails!
                .Where(t => !string.IsNullOrEmpty(t.Url))
                .Select(t => new ThumbnailInfo(t.ID, t.Url!, t.Width, t.Height, t.Preference))
                .ToList();

        if (list.Count == 0 && !string.IsNullOrEmpty(fallbackUrl))
        {
            list.Add(new ThumbnailInfo("default", fallbackUrl, null, null, null));
        }

        return list;
    }

    private async Task<List<VideoMetadata>> MapEntriesToVideoMetadataAsync(
        VideoData[]? entries,
        string channelId,
        string channelName,
        CancellationToken cancellationToken)
    {
        if (entries is null or { Length: 0 })
        {
            return [];
        }

        // Sequential by design (polite to Rumble + avoids bursting the oEmbed endpoint).
        var results = new List<VideoMetadata>(entries.Length);
        foreach (var x in entries)
        {
            // yt-dlp's Rumble channel extractor can yield null placeholder entries
            // (e.g. when a page is blocked); skip anything we can't identify.
            if (x is null)
            {
                continue;
            }

            string url = x.WebpageUrl ?? x.Url ?? string.Empty;
            string videoId = !string.IsNullOrEmpty(x.ID) ? x.ID : ExtractRumbleVideoId(url);
            if (string.IsNullOrEmpty(videoId))
            {
                continue;
            }

            // Flat-playlist entries from Rumble's channel extractor carry nothing but a URL, so
            // enrich each one via oEmbed to get a real title/thumbnail/duration.
            var oembed = string.IsNullOrEmpty(url) ? null : await GetOEmbedAsync(url, cancellationToken);

            results.Add(new VideoMetadata
            {
                VideoId = videoId,
                Title = x.Title ?? oembed?.Title ?? DeriveTitleFromRumbleUrl(url) ?? "Unknown Title",
                Description = x.Description,
                Url = url,
                ThumbnailUrl = PickThumbnail(x.Thumbnails, x.Thumbnail ?? oembed?.ThumbnailUrl),
                Duration = x.Duration.HasValue
                    ? TimeSpan.FromSeconds(x.Duration.Value)
                    : oembed?.Duration.HasValue == true ? TimeSpan.FromSeconds(oembed.Duration.Value) : null,
                UploadDate = (x.UploadDate ?? x.Timestamp).AsUtc(),
                ViewCount = x.ViewCount.HasValue ? (int?)x.ViewCount : null,
                LikeCount = x.LikeCount.HasValue ? (int?)x.LikeCount : null,
                ChannelId = channelId,
                ChannelName = oembed?.AuthorName ?? channelName,
                Platform = PlatformName,
                PlaylistId = null
            });
        }

        return results;
    }

    /// <summary>
    /// Shape of https://rumble.com/api/Media/oembed.json — standard oEmbed fields use snake_case.
    /// </summary>
    private sealed class RumbleOEmbedResult
    {
        public string? Title { get; set; }

        [JsonPropertyName("author_name")]
        public string? AuthorName { get; set; }

        [JsonPropertyName("author_url")]
        public string? AuthorUrl { get; set; }

        [JsonPropertyName("thumbnail_url")]
        public string? ThumbnailUrl { get; set; }

        public int? Duration { get; set; }
    }
}
