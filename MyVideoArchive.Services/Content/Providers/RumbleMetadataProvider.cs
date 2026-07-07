using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using MyVideoArchive.Models.Metadata;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
using YoutubeDLSharp.Options;

namespace MyVideoArchive.Services.Content.Providers;

/// <summary>
/// Rumble implementation of metadata provider.
/// Supports channel URLs (rumble.com/c/{id} and rumble.com/user/{id}), playlist URLs
/// (rumble.com/playlists/{id}) and single video URLs.
///
/// yt-dlp's Rumble channel extractor no longer works: Rumble's channel video grid is rendered
/// client-side now, so the static HTML it downloads contains no video links at all (except for
/// one server-rendered "Featured" video pinned to the top, which is all yt-dlp ever finds,
/// regardless of how many videos the channel actually has). The real video grid ships instead as
/// JSON embedded in a &lt;script type="application/json"&gt; block on the /videos tab, so this
/// provider scrapes and paginates that directly. The /playlists tab and playlist detail pages
/// are server-rendered HTML cards, scraped with HtmlAgilityPack. yt-dlp is kept only as a
/// last-resort fallback.
///
/// All scraping goes through the named "<see cref="HttpClientName"/>" HttpClient, which is
/// configured (in ServiceCollectionExtensions) with a full browser-like header set and gzip
/// decompression — Rumble's Cloudflare protection returns 403 to requests that carry only a
/// User-Agent with no Accept/Accept-Language/Accept-Encoding headers.
/// </summary>
public partial class RumbleMetadataProvider : IVideoMetadataProvider
{
    /// <summary>Name of the pre-configured HttpClient used for scraping rumble.com.</summary>
    public const string HttpClientName = "Rumble";

    private const int MaxChannelPages = 500;

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
        httpClient = httpClientFactory.CreateClient(HttpClientName);
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

            string channelId = FirstNonEmpty(
                RumbleChannelIdRegex().Match(channelUrl).Groups[1].Value,
                channelUrl.TrimEnd('/').Split('/').LastOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? string.Empty);

            string baseUrl = NormalizeChannelBaseUrl(channelUrl);
            string? html = await FetchHtmlAsync($"{baseUrl}/videos?page=1", cancellationToken);

            if (html is not null)
            {
                string? channelName = null;
                string? firstVideoThumbnail = null;

                var items = ExtractGridItems(html);
                if (items is not null)
                {
                    foreach (var item in items)
                    {
                        if (!IsVideoItem(item))
                        {
                            continue;
                        }

                        channelName = AsString(item?["by"]?["name"]);
                        firstVideoThumbnail = AsString(item?["thumb"]);
                        break;
                    }
                }

                var (avatarUrl, bannerUrl) = ExtractHeaderImages(html);

                if (!string.IsNullOrEmpty(channelName) || !string.IsNullOrEmpty(avatarUrl) || !string.IsNullOrEmpty(bannerUrl))
                {
                    return new ChannelMetadata
                    {
                        ChannelId = channelId,
                        Name = string.IsNullOrEmpty(channelName)
                            ? (string.IsNullOrEmpty(channelId) ? "Unknown Channel" : channelId)
                            : channelName,
                        Url = channelUrl,
                        Description = null,
                        BannerUrl = FirstNonEmpty(bannerUrl, avatarUrl, firstVideoThumbnail),
                        AvatarUrl = FirstNonEmpty(avatarUrl, bannerUrl, firstVideoThumbnail),
                        Thumbnails = BuildThumbnailList(avatarUrl, bannerUrl, firstVideoThumbnail),
                        SubscriberCount = null,
                        Platform = PlatformName
                    };
                }
            }

            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("Rumble page scrape found nothing for {Url}; falling back to yt-dlp", channelUrl);
            }

            return await GetChannelMetadataViaYtDlpAsync(channelUrl, cancellationToken);
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
    /// Scrapes the channel's /playlists tab (server-rendered HTML cards; yt-dlp has no Rumble
    /// playlist extractor at all). Paginates with ?page=N until a page adds nothing new.
    /// </summary>
    public async Task<List<PlaylistMetadata>> GetChannelPlaylistsAsync(string channelUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Scraping Rumble channel playlists for: {Url}", channelUrl);
            }

            string channelId = FirstNonEmpty(
                RumbleChannelIdRegex().Match(channelUrl).Groups[1].Value,
                channelUrl.TrimEnd('/').Split('/').LastOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? string.Empty);

            string baseUrl = NormalizeChannelBaseUrl(channelUrl);
            var playlists = new Dictionary<string, PlaylistMetadata>(StringComparer.Ordinal);

            for (int page = 1; page <= MaxChannelPages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string? html = await FetchHtmlAsync($"{baseUrl}/playlists?page={page}", cancellationToken);
                if (html is null)
                {
                    break;
                }

                int addedThisPage = ParsePlaylistCards(html, channelId, playlists);
                if (addedThisPage == 0)
                {
                    break;
                }
            }

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Scraped {Count} playlists for Rumble channel: {Url}", playlists.Count, channelUrl);
            }

            return [.. playlists.Values];
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error scraping Rumble channel playlists for {Url}", channelUrl);
            }
            return [];
        }
    }

    public async Task<List<VideoMetadata>> GetChannelVideosAsync(string channelUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Fetching Rumble channel videos for: {Url}", channelUrl);
            }

            string channelId = FirstNonEmpty(
                RumbleChannelIdRegex().Match(channelUrl).Groups[1].Value,
                channelUrl.TrimEnd('/').Split('/').LastOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? string.Empty);

            var videos = await ScrapeChannelGridAsync(NormalizeChannelBaseUrl(channelUrl), channelId, cancellationToken);

            if (videos.Count > 0)
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Scraped {Count} videos from Rumble channel grid for {Url}", videos.Count, channelUrl);
                }

                return videos;
            }

            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("Rumble grid scrape returned no videos for {Url}; falling back to yt-dlp", channelUrl);
            }

            return await GetChannelVideosViaYtDlpAsync(channelUrl, cancellationToken);
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

    /// <summary>
    /// Scrapes a playlist detail page (rumble.com/playlists/{id}) for its metadata. yt-dlp has no
    /// Rumble playlist extractor, so scraping is the only option; the page is server-rendered and
    /// carries standard OpenGraph meta tags.
    /// </summary>
    public async Task<PlaylistMetadata?> GetPlaylistMetadataAsync(string playlistUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Fetching Rumble playlist metadata for: {Url}", playlistUrl);
            }

            string playlistId = RumblePlaylistIdRegex().Match(playlistUrl).Groups[1].Value;

            string? html = await FetchHtmlAsync(StripQuery(playlistUrl), cancellationToken);
            if (html is null)
            {
                return null;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            string? title = GetMetaContent(doc, "og:title")
                ?? HtmlEntity.DeEntitize(doc.DocumentNode.SelectSingleNode("//h1")?.InnerText ?? string.Empty).Trim();
            string? description = GetMetaContent(doc, "og:description");
            string? thumbnailUrl = GetMetaContent(doc, "og:image");

            // The channel link on a playlist page points back at /c/{id} or /user/{id}.
            var channelAnchor = doc.DocumentNode.SelectSingleNode("//a[starts-with(@href, '/c/') or starts-with(@href, '/user/')]");
            string channelId = channelAnchor is not null
                ? RumbleChannelIdRegex().Match("https://rumble.com" + channelAnchor.GetAttributeValue("href", string.Empty)).Groups[1].Value
                : string.Empty;
            string channelName = channelAnchor is not null
                ? HtmlEntity.DeEntitize(channelAnchor.InnerText).Trim()
                : string.Empty;

            return new PlaylistMetadata
            {
                PlaylistId = string.IsNullOrEmpty(playlistId) ? StripQuery(playlistUrl).Split('/').Last() : playlistId,
                Name = string.IsNullOrWhiteSpace(title) ? "Unknown Playlist" : title,
                Url = StripQuery(playlistUrl),
                ThumbnailUrl = thumbnailUrl,
                Description = string.IsNullOrWhiteSpace(description) ? null : description,
                ChannelId = channelId,
                ChannelName = string.IsNullOrWhiteSpace(channelName) ? "Unknown Channel" : channelName,
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

    /// <summary>
    /// Scrapes a playlist detail page for its videos. Prefers the embedded JSON grid (same shape
    /// as channel /videos pages) when present; otherwise falls back to parsing the
    /// server-rendered video cards. Paginates with ?page=N until a page adds nothing new (the
    /// page serves the same first batch for out-of-range page numbers, so the dedup check
    /// terminates the loop).
    /// </summary>
    public async Task<List<VideoMetadata>> GetPlaylistVideosAsync(string playlistUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Fetching Rumble playlist videos for: {Url}", playlistUrl);
            }

            string baseUrl = StripQuery(playlistUrl);
            var videos = new List<VideoMetadata>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? channelName = null;

            for (int page = 1; page <= MaxChannelPages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string? html = await FetchHtmlAsync($"{baseUrl}?page={page}", cancellationToken);
                if (html is null)
                {
                    break;
                }

                int addedThisPage = 0;

                var items = ExtractGridItems(html);
                if (items is not null)
                {
                    foreach (var item in items)
                    {
                        var video = MapGridItemToVideo(item, string.Empty, channelName ?? "Unknown Channel");
                        if (video is null || !seenIds.Add(video.VideoId))
                        {
                            continue;
                        }

                        channelName ??= video.ChannelName;
                        videos.Add(video);
                        addedThisPage++;
                    }
                }
                else
                {
                    foreach (var video in ParseVideoCards(html, channelName ?? "Unknown Channel"))
                    {
                        if (!seenIds.Add(video.VideoId))
                        {
                            continue;
                        }

                        channelName ??= video.ChannelName;
                        videos.Add(video);
                        addedThisPage++;
                    }
                }

                if (addedThisPage == 0)
                {
                    break;
                }
            }

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Scraped {Count} videos from Rumble playlist: {Url}", videos.Count, playlistUrl);
            }

            return videos;
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

    // ── Grid scraping (primary path for channel videos/images) ─────────────────────────────────

    /// <summary>
    /// Pages through a Rumble channel's video grid (<c>?page=1,2,3…</c>) until a page yields no
    /// video items, collecting every video along the way. This is the only way to see more than
    /// the single server-rendered "Featured" video, since the rest of the grid is populated
    /// client-side and invisible to yt-dlp's plain HTML fetch.
    /// </summary>
    private async Task<List<VideoMetadata>> ScrapeChannelGridAsync(string baseChannelUrl, string channelId, CancellationToken cancellationToken)
    {
        var videos = new List<VideoMetadata>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? channelName = null;

        for (int page = 1; page <= MaxChannelPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? html = await FetchHtmlAsync($"{baseChannelUrl}/videos?page={page}", cancellationToken);
            if (html is null)
            {
                break;
            }

            var items = ExtractGridItems(html);
            if (items is null)
            {
                break;
            }

            int addedThisPage = 0;
            foreach (var item in items)
            {
                var video = MapGridItemToVideo(item, channelId, channelName ?? "Unknown Channel");
                if (video is null || !seenIds.Add(video.VideoId))
                {
                    continue;
                }

                channelName ??= video.ChannelName;
                videos.Add(video);
                addedThisPage++;
            }

            if (addedThisPage == 0)
            {
                break;
            }
        }

        return videos;
    }

    /// <summary>
    /// Downloads a page's HTML with a browser-like User-Agent (required — Rumble blocks
    /// non-browser clients). Returns null on any failure so callers can fall back gracefully.
    /// </summary>
    private async Task<string?> FetchHtmlAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("Rumble page fetch failed for {Url}: {StatusCode}", url, response.StatusCode);
                }
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(ex, "Error fetching Rumble page {Url}", url);
            }
            return null;
        }
    }

    /// <summary>
    /// Rumble's channel/videos grid is rendered client-side; the actual video list ships as JSON
    /// inside one of several embedded <c>&lt;script type="application/json"&gt;</c> blocks on the
    /// page (an object with an "items" array). This finds that block, if present.
    /// </summary>
    private static JsonArray? ExtractGridItems(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var scriptNodes = doc.DocumentNode.SelectNodes("//script[@type='application/json']");
        if (scriptNodes is null)
        {
            return null;
        }

        foreach (var node in scriptNodes)
        {
            string json = node.InnerHtml;
            if (string.IsNullOrWhiteSpace(json) || !json.Contains("\"items\"", StringComparison.Ordinal))
            {
                continue;
            }

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(json);
            }
            catch (JsonException)
            {
                continue;
            }

            if (root?["items"] is JsonArray items)
            {
                return items;
            }
        }

        return null;
    }

    private static bool IsVideoItem(JsonNode? item) =>
        item is not null && string.Equals(AsString(item["object_type"]), "video", StringComparison.OrdinalIgnoreCase);

    private VideoMetadata? MapGridItemToVideo(JsonNode? item, string channelId, string fallbackChannelName)
    {
        if (!IsVideoItem(item))
        {
            return null;
        }

        string relativeUrl = AsString(item!["url"]) ?? string.Empty;
        if (string.IsNullOrEmpty(relativeUrl))
        {
            return null;
        }

        string url = relativeUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? relativeUrl
            : "https://rumble.com" + relativeUrl;

        string videoId = ExtractRumbleVideoId(url);
        if (string.IsNullOrEmpty(videoId))
        {
            return null;
        }

        double? duration = AsDouble(item["duration"]);
        double? views = AsDouble(item["views"]);
        string? itemChannelName = AsString(item["by"]?["name"]);
        DateTime? uploadDate = ParseIsoDate(AsString(item["upload_date"]));

        return new VideoMetadata
        {
            VideoId = videoId,
            Title = AsString(item["title"]) ?? DeriveTitleFromRumbleUrl(url) ?? "Unknown Title",
            Description = null,
            Url = url,
            ThumbnailUrl = AsString(item["thumb"]),
            Duration = duration.HasValue && duration.Value > 0 ? TimeSpan.FromSeconds(duration.Value) : null,
            UploadDate = uploadDate,
            ViewCount = views.HasValue ? (int?)views.Value : null,
            LikeCount = null,
            ChannelId = channelId,
            ChannelName = itemChannelName ?? fallbackChannelName,
            Platform = PlatformName,
            PlaylistId = null
        };
    }

    /// <summary>
    /// Best-effort scrape of the channel header's cover/avatar images for the "Select Channel
    /// Images" picker. These two images are server-rendered (unlike the video grid) and are
    /// served from Rumble's user-content CDN (<c>1a-1791.com</c> / <c>*.rmbl.ws</c>), which
    /// distinguishes them from logos/ad assets elsewhere on the page.
    /// </summary>
    private static (string? AvatarUrl, string? BannerUrl) ExtractHeaderImages(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var imgNodes = doc.DocumentNode.SelectNodes("//img[@src]");
        if (imgNodes is null)
        {
            return (null, null);
        }

        var candidates = imgNodes
            .Select(n => n.GetAttributeValue("src", string.Empty))
            .Where(src => !string.IsNullOrWhiteSpace(src) && IsChannelCdnImage(src))
            .Distinct()
            .ToList();

        // Rumble renders exactly two header images in this order: cover (banner) then avatar.
        string? bannerUrl = candidates.ElementAtOrDefault(0);
        string? avatarUrl = candidates.ElementAtOrDefault(1) ?? bannerUrl;

        return (avatarUrl, bannerUrl);
    }

    private static bool IsChannelCdnImage(string src) =>
        src.Contains("1a-1791.com", StringComparison.OrdinalIgnoreCase)
        || src.Contains(".rmbl.ws", StringComparison.OrdinalIgnoreCase);

    private static List<ThumbnailInfo> BuildThumbnailList(string? avatarUrl, string? bannerUrl, string? fallbackUrl)
    {
        var list = new List<ThumbnailInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AddIfNew(string id, string? url)
        {
            if (!string.IsNullOrEmpty(url) && seen.Add(url))
            {
                list.Add(new ThumbnailInfo(id, url, null, null, null));
            }
        }

        AddIfNew("banner", bannerUrl);
        AddIfNew("avatar", avatarUrl);
        AddIfNew("default", fallbackUrl);

        return list;
    }

    private static string? AsString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out string? s) ? s : null;

    private static double? AsDouble(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<double>(out double d))
        {
            return d;
        }

        return value.TryGetValue<string>(out string? s) && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : null;
    }

    private static DateTime? ParseIsoDate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.UtcDateTime
            : null;
    }

    private static string StripQuery(string url) => url.Split('?', '#')[0].TrimEnd('/');

    /// <summary>
    /// Normalizes a channel URL to its bare form (no query, no trailing tab segment) so tab
    /// paths like /videos and /playlists can be appended safely, whether the stored channel URL
    /// is "…/c/Name", "…/c/Name/videos" or has query parameters.
    /// </summary>
    private static string NormalizeChannelBaseUrl(string channelUrl)
    {
        string url = StripQuery(channelUrl);

        foreach (string tab in new[] { "/videos", "/playlists", "/livestreams", "/shorts", "/about" })
        {
            if (url.EndsWith(tab, StringComparison.OrdinalIgnoreCase))
            {
                url = url[..^tab.Length];
                break;
            }
        }

        return url.TrimEnd('/');
    }

    /// <summary>
    /// Parses the server-rendered playlist cards on a channel's /playlists tab. Each card links
    /// to /playlists/{id} (several times — thumbnail and title anchors), so results are keyed by
    /// playlist id and the best title/thumbnail found across anchors wins. Returns the number of
    /// playlists newly added from this page.
    /// </summary>
    private int ParsePlaylistCards(string html, string channelId, Dictionary<string, PlaylistMetadata> playlists)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var anchors = doc.DocumentNode.SelectNodes("//a[contains(@href, '/playlists/')]");
        if (anchors is null)
        {
            return 0;
        }

        // Each card links to its playlist several times (thumbnail anchor, title anchor, …). The
        // thumbnail anchor's text is junk like "125 videos", so only a heading or an explicit
        // title attribute counts as a "strong" title; bare anchor text is a last resort that a
        // later strong title may overwrite.
        var strongTitled = new HashSet<string>(StringComparer.Ordinal);

        int added = 0;
        foreach (var anchor in anchors)
        {
            string href = anchor.GetAttributeValue("href", string.Empty);
            var match = RumblePlaylistIdRegex().Match(href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? href
                : "https://rumble.com" + href);
            if (!match.Success)
            {
                continue;
            }

            string playlistId = match.Groups[1].Value;
            var (strongTitle, weakTitle) = GetAnchorTitles(anchor);
            string? thumbnailUrl = anchor.SelectSingleNode(".//img[@src]")?.GetAttributeValue("src", null);

            if (!playlists.TryGetValue(playlistId, out var existing))
            {
                playlists[playlistId] = new PlaylistMetadata
                {
                    PlaylistId = playlistId,
                    Name = strongTitle ?? weakTitle ?? "Unknown Playlist",
                    Url = $"https://rumble.com/playlists/{playlistId}",
                    ThumbnailUrl = thumbnailUrl,
                    Description = null,
                    ChannelId = channelId,
                    ChannelName = channelId,
                    Platform = PlatformName
                };

                if (strongTitle is not null)
                {
                    strongTitled.Add(playlistId);
                }

                added++;
            }
            else
            {
                if (strongTitle is not null && strongTitled.Add(playlistId))
                {
                    existing.Name = strongTitle;
                }

                existing.ThumbnailUrl ??= thumbnailUrl;
            }
        }

        return added;
    }

    private static readonly string[] HeadingTags = ["h1", "h2", "h3", "h4"];

    /// <summary>
    /// Extracts title candidates from a card anchor: "strong" (explicit title attribute, a
    /// heading element inside the anchor, or the anchor itself sitting inside a heading) vs
    /// "weak" (the anchor's bare text, which on thumbnail anchors is junk like a video count or
    /// duration).
    /// </summary>
    private static (string? Strong, string? Weak) GetAnchorTitles(HtmlNode anchor)
    {
        bool inHeading = anchor.Ancestors().Any(a => HeadingTags.Contains(a.Name, StringComparer.OrdinalIgnoreCase));

        string? strong = FirstNonEmptyOrNull(
            anchor.GetAttributeValue("title", null),
            HtmlEntity.DeEntitize(anchor.SelectSingleNode(".//h1|.//h2|.//h3|.//h4")?.InnerText ?? string.Empty).Trim(),
            inHeading ? HtmlEntity.DeEntitize(anchor.InnerText).Trim() : null);

        string? weak = FirstNonEmptyOrNull(HtmlEntity.DeEntitize(anchor.InnerText).Trim());

        return (strong, weak);
    }

    /// <summary>
    /// Parses server-rendered video cards (used on playlist detail pages, which — unlike channel
    /// /videos tabs — do not embed a JSON grid). Cards link to /v{id}-{slug}.html multiple times;
    /// results are grouped by video id with the best title/thumbnail/duration found.
    /// </summary>
    private List<VideoMetadata> ParseVideoCards(string html, string fallbackChannelName)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var anchors = doc.DocumentNode.SelectNodes("//a[@href]");
        if (anchors is null)
        {
            return [];
        }

        var byId = new Dictionary<string, VideoMetadata>(StringComparer.OrdinalIgnoreCase);
        var strongTitled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var anchor in anchors)
        {
            string href = anchor.GetAttributeValue("href", string.Empty);
            string absolute = href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? href
                : "https://rumble.com" + href;

            string videoId = ExtractRumbleVideoId(absolute);
            if (string.IsNullOrEmpty(videoId))
            {
                continue;
            }

            string url = StripQuery(absolute);
            var (strongTitle, _) = GetAnchorTitles(anchor);
            string? thumbnailUrl = anchor.SelectSingleNode(".//img[@src]")?.GetAttributeValue("src", null);

            if (!byId.TryGetValue(videoId, out var existing))
            {
                // No weak-title fallback here: the URL slug gives a decent title, while a
                // thumbnail anchor's bare text is a duration/view count.
                byId[videoId] = new VideoMetadata
                {
                    VideoId = videoId,
                    Title = strongTitle ?? DeriveTitleFromRumbleUrl(url) ?? "Unknown Title",
                    Description = null,
                    Url = url,
                    ThumbnailUrl = thumbnailUrl,
                    Duration = null,
                    UploadDate = null,
                    ViewCount = null,
                    LikeCount = null,
                    ChannelId = string.Empty,
                    ChannelName = fallbackChannelName,
                    Platform = PlatformName,
                    PlaylistId = null
                };

                if (strongTitle is not null)
                {
                    strongTitled.Add(videoId);
                }
            }
            else
            {
                if (strongTitle is not null && strongTitled.Add(videoId))
                {
                    existing.Title = strongTitle;
                }

                existing.ThumbnailUrl ??= thumbnailUrl;
            }
        }

        return [.. byId.Values];
    }

    private static string? GetMetaContent(HtmlDocument doc, string property)
    {
        var node = doc.DocumentNode.SelectSingleNode($"//meta[@property='{property}']")
            ?? doc.DocumentNode.SelectSingleNode($"//meta[@name='{property}']");
        string? content = node?.GetAttributeValue("content", null);
        return string.IsNullOrWhiteSpace(content) ? null : HtmlEntity.DeEntitize(content).Trim();
    }

    private static string? FirstNonEmptyOrNull(params string?[] values)
    {
        string result = FirstNonEmpty(values);
        return string.IsNullOrEmpty(result) ? null : result;
    }

    // ── yt-dlp fallback path (used only if the grid scrape above finds nothing at all) ─────────

    private async Task<ChannelMetadata?> GetChannelMetadataViaYtDlpAsync(string channelUrl, CancellationToken cancellationToken)
    {
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

        var firstEntryUrl = data.Entries?.FirstOrDefault(e => e is not null) is { } entry
            ? entry.WebpageUrl ?? entry.Url
            : null;
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

    private async Task<List<VideoMetadata>> GetChannelVideosViaYtDlpAsync(string channelUrl, CancellationToken cancellationToken)
    {
        var options = new OptionSet
        {
            FlatPlaylist = true,
            DumpSingleJson = true,
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

        return await MapEntriesToVideoMetadataAsync(result.Data.Entries, channelId, channelName, cancellationToken);
    }

    [GeneratedRegex(@"rumble\.com/(?:c|user)/([^/?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RumbleChannelIdRegex();

    [GeneratedRegex(@"rumble\.com", RegexOptions.IgnoreCase)]
    private static partial Regex RumbleUrlRegex();

    // Rumble video URLs look like https://rumble.com/v6abc12-some-title-slug.html
    // (or /embed/v6abc12/). The native video id is the leading "v…" token.
    [GeneratedRegex(@"rumble\.com/(?:embed/)?(v[0-9a-z]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RumbleVideoIdRegex();

    // Playlist URLs look like https://rumble.com/playlists/Rq8vmQLCz-Q (base64url-style id).
    [GeneratedRegex(@"rumble\.com/playlists/([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RumblePlaylistIdRegex();

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
    /// when the grid JSON (or, in the yt-dlp fallback path, oEmbed) doesn't provide one. e.g.
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
    /// (author) name, thumbnail and duration. Only used by the yt-dlp fallback path.
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
    /// Builds the thumbnail candidate list shown in the "Select Channel Images" picker, for the
    /// yt-dlp fallback path.
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
