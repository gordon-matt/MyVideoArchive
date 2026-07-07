using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using MyVideoArchive.Models.Metadata;

namespace MyVideoArchive.Services.Content.Providers;

/// <summary>
/// HTML/JSON parsing helpers for rumble.com pages. Kept separate from
/// <see cref="RumbleMetadataProvider"/> so the provider stays focused on orchestration.
/// </summary>
internal static partial class RumblePageParser
{
    private static readonly string[] ChannelTabSuffixes =
        ["/videos", "/playlists", "/livestreams", "/shorts", "/about"];

    private static readonly string[] HeadingTags = ["h1", "h2", "h3", "h4"];

    private static readonly string[] ImageUrlAttributes =
        ["src", "data-src", "data-lazy-src", "data-original", "data-thumb"];

    internal static bool IsSystemPlaylist(string? name) =>
        !string.IsNullOrWhiteSpace(name) && (
            name.Equals("Watch Later", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Watch History", StringComparison.OrdinalIgnoreCase));

    internal static string NormalizeChannelBaseUrl(string channelUrl)
    {
        string url = StripQuery(channelUrl);

        foreach (string tab in ChannelTabSuffixes)
        {
            if (url.EndsWith(tab, StringComparison.OrdinalIgnoreCase))
            {
                url = url[..^tab.Length];
                break;
            }
        }

        return url.TrimEnd('/');
    }

    internal static string StripQuery(string url) => url.Split('?', '#')[0].TrimEnd('/');

    internal static string ExtractRumbleVideoId(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return string.Empty;
        }

        var match = RumbleVideoIdRegex().Match(url);
        if (!match.Success)
        {
            return string.Empty;
        }

        string id = match.Groups[1].Value;
        return RumbleVideoIdFormatRegex().IsMatch(id) ? id : string.Empty;
    }

    /// <summary>
    /// Collects every video belonging to a playlist detail page by merging three sources:
    /// the homogeneous JSON grid (playlist entries, not mixed-channel recommendations),
    /// <c>playlist_id</c>-tagged anchor links, and same-channel video links in the page
    /// <c>&lt;main&gt;</c> area (some entries omit <c>playlist_id</c> from the href).
    /// </summary>
    internal static List<VideoMetadata> ParsePlaylistVideos(
        string html,
        string playlistId,
        string? channelName,
        string platformName)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        channelName = ResolvePlaylistChannelName(doc, html, channelName);
        string channelLabel = channelName ?? "Unknown Channel";

        var byId = new Dictionary<string, VideoMetadata>(StringComparer.OrdinalIgnoreCase);

        foreach (var video in ParseVideostreamList(doc, playlistId, channelLabel, platformName))
        {
            MergeVideo(byId, video);
        }

        foreach (var item in FindHomogeneousVideoGrid(html, channelName) ?? Enumerable.Empty<JsonNode?>())
        {
            var video = MapGridItemToVideo(item, string.Empty, channelLabel, platformName);
            if (video is not null)
            {
                video.PlaylistId = playlistId;
                MergeVideo(byId, video);
            }
        }

        foreach (var anchor in EnumeratePlaylistAnchors(doc))
        {
            string absolute = ToAbsoluteUrl(DecodeHref(anchor));
            if (!IsPlaylistVideoHref(anchor, absolute, playlistId))
            {
                continue;
            }

            AddAnchorVideo(byId, anchor, absolute, playlistId, channelLabel, platformName);
        }

        return [.. byId.Values];
    }

    internal static int ParsePlaylistCards(
        string html,
        string channelId,
        string platformName,
        Dictionary<string, PlaylistMetadata> playlists)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var anchors = doc.DocumentNode.SelectNodes("//a[contains(@href, '/playlists/')]");
        if (anchors is null)
        {
            return 0;
        }

        var strongTitled = new HashSet<string>(StringComparer.Ordinal);
        int added = 0;

        foreach (var anchor in anchors)
        {
            string href = DecodeHref(anchor);
            var match = RumblePlaylistIdRegex().Match(ToAbsoluteUrl(href));
            if (!match.Success)
            {
                continue;
            }

            string playlistId = match.Groups[1].Value;
            var (strongTitle, weakTitle) = GetAnchorTitles(anchor);

            if (IsSystemPlaylist(strongTitle ?? weakTitle))
            {
                continue;
            }

            string? thumbnailUrl = GetImageUrl(anchor) ?? FindNearbyImage(anchor);

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
                    Platform = platformName
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

    internal static JsonArray? ExtractFirstGridItems(string html) =>
        ExtractAllGridItemArrays(html).FirstOrDefault();

    internal static IEnumerable<JsonArray> ExtractAllGridItemArrays(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var scriptNodes = doc.DocumentNode.SelectNodes("//script[@type='application/json']");
        if (scriptNodes is null)
        {
            yield break;
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
                yield return items;
            }
        }
    }

    internal static VideoMetadata? MapGridItemToVideo(
        JsonNode? item,
        string channelId,
        string fallbackChannelName,
        string platformName)
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
            Platform = platformName,
            PlaylistId = null
        };
    }

    internal static (string? AvatarUrl, string? BannerUrl) ExtractHeaderImages(string html)
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

        string? bannerUrl = candidates.ElementAtOrDefault(0);
        string? avatarUrl = candidates.ElementAtOrDefault(1) ?? bannerUrl;

        return (avatarUrl, bannerUrl);
    }

    internal static List<ThumbnailInfo> BuildThumbnailList(string? avatarUrl, string? bannerUrl, string? fallbackUrl)
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

    internal static string? GetMetaContent(HtmlDocument doc, string property)
    {
        var node = doc.DocumentNode.SelectSingleNode($"//meta[@property='{property}']")
            ?? doc.DocumentNode.SelectSingleNode($"//meta[@name='{property}']");
        string? content = node?.GetAttributeValue("content", null);
        return string.IsNullOrWhiteSpace(content) ? null : HtmlEntity.DeEntitize(content).Trim();
    }

    internal static string? GetImageUrl(HtmlNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node.Name.Equals("img", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string attr in ImageUrlAttributes)
            {
                string? url = NormalizeImageUrl(node.GetAttributeValue(attr, null));
                if (IsUsableImageUrl(url))
                {
                    return url;
                }
            }

            string? srcset = node.GetAttributeValue("srcset", null);
            if (!string.IsNullOrWhiteSpace(srcset))
            {
                string first = srcset.Split(',')[0].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                string? url = NormalizeImageUrl(first);
                if (IsUsableImageUrl(url))
                {
                    return url;
                }
            }
        }

        string? style = node.GetAttributeValue("style", null);
        if (!string.IsNullOrWhiteSpace(style))
        {
            var match = BackgroundImageUrlRegex().Match(style);
            if (match.Success)
            {
                string? url = NormalizeImageUrl(match.Groups[1].Value);
                if (IsUsableImageUrl(url))
                {
                    return url;
                }
            }
        }

        return null;
    }

    internal static string? FindNearbyImage(HtmlNode anchor)
    {
        for (HtmlNode? current = anchor; current is not null; current = current.ParentNode)
        {
            foreach (var img in current.SelectNodes(".//img") ?? Enumerable.Empty<HtmlNode>())
            {
                string? url = GetImageUrl(img);
                if (IsUsableImageUrl(url))
                {
                    return url;
                }
            }
        }

        return null;
    }

    internal static string? DeriveTitleFromRumbleUrl(string url)
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

    internal static string? ExtractPlaylistChannelName(HtmlDocument doc)
    {
        // Prefer the channel link in the playlist header (near the h1), not sidebar recommendations.
        var headerChannel = doc.DocumentNode.SelectSingleNode(
            "//h1/ancestor::*[contains(@class,'header') or contains(@class,'playlist')][1]"
            + "//a[starts-with(@href, '/c/') or starts-with(@href, '/user/')]");
        string? fromHeader = ChannelNameFromAnchor(headerChannel);
        if (!string.IsNullOrEmpty(fromHeader))
        {
            return fromHeader;
        }

        var h1 = doc.DocumentNode.SelectSingleNode("//h1");
        if (h1 is not null)
        {
            for (HtmlNode? node = h1.ParentNode; node is not null; node = node.ParentNode)
            {
                foreach (var anchor in node.SelectNodes(".//a[starts-with(@href, '/c/') or starts-with(@href, '/user/')]")
                             ?? Enumerable.Empty<HtmlNode>())
                {
                    string? name = ChannelNameFromAnchor(anchor);
                    if (!string.IsNullOrEmpty(name))
                    {
                        return name;
                    }
                }

                if (node.Name.Equals("main", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
        }

        return ChannelNameFromAnchor(
            doc.DocumentNode.SelectSingleNode("//a[starts-with(@href, '/c/') or starts-with(@href, '/user/')]"));
    }

    /// <summary>
    /// Picks the embedded JSON <c>items</c> array that represents the playlist itself: every
    /// entry is a video from a single channel. Recommendation sidebars mix many channels and
    /// are ignored.
    /// </summary>
    internal static IEnumerable<JsonNode?> FindHomogeneousVideoGrid(string html, string? preferredChannelName)
    {
        JsonArray? bestPreferred = null;
        int bestPreferredCount = 0;
        JsonArray? bestAny = null;
        int bestAnyCount = 0;

        foreach (var items in ExtractAllGridItemArrays(html))
        {
            var videos = items.Where(IsVideoItem).ToList();
            if (videos.Count == 0)
            {
                continue;
            }

            var channelNames = videos
                .Select(v => AsString(v?["by"]?["name"]))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (channelNames.Count > 1)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(preferredChannelName)
                && channelNames.Count == 1
                && channelNames[0].Equals(preferredChannelName, StringComparison.OrdinalIgnoreCase)
                && videos.Count > bestPreferredCount)
            {
                bestPreferred = items;
                bestPreferredCount = videos.Count;
            }

            if (channelNames.Count <= 1 && videos.Count > bestAnyCount)
            {
                bestAny = items;
                bestAnyCount = videos.Count;
            }
        }

        JsonArray? chosen = bestPreferred ?? bestAny;
        if (chosen is null)
        {
            yield break;
        }

        foreach (var item in chosen)
        {
            if (IsVideoItem(item))
            {
                yield return item;
            }
        }
    }

    private static string? ResolvePlaylistChannelName(HtmlDocument doc, string html, string? channelName)
    {
        if (!string.IsNullOrWhiteSpace(channelName))
        {
            return channelName;
        }

        channelName = ExtractPlaylistChannelName(doc);
        if (!string.IsNullOrWhiteSpace(channelName))
        {
            return channelName;
        }

        foreach (var item in FindHomogeneousVideoGrid(html, null))
        {
            string? fromItem = AsString(item?["by"]?["name"]);
            if (!string.IsNullOrWhiteSpace(fromItem))
            {
                return fromItem;
            }
        }

        return null;
    }

    private static IEnumerable<VideoMetadata> ParseVideostreamList(
        HtmlDocument doc,
        string playlistId,
        string channelLabel,
        string platformName)
    {
        var list = doc.DocumentNode.SelectSingleNode(
            $"//ol[contains(@class,'videostream__list') and @data-playlist='{playlistId}']");
        if (list is null)
        {
            yield break;
        }

        foreach (var item in list.SelectNodes("./li") ?? Enumerable.Empty<HtmlNode>())
        {
            var link = item.SelectSingleNode(".//a[contains(@class,'videostream__link')]")
                ?? item.SelectSingleNode(".//a[contains(@class,'title__link')]")
                ?? item.SelectSingleNode(".//a[contains(@href, '/v') and contains(@href, '.html')]");
            if (link is null)
            {
                continue;
            }

            string absolute = ToAbsoluteUrl(DecodeHref(link));
            string videoId = ExtractRumbleVideoId(absolute);
            if (string.IsNullOrEmpty(videoId))
            {
                continue;
            }

            string url = StripQuery(absolute);
            var titleNode = item.SelectSingleNode(".//h3[contains(@class,'thumbnail__title')]");
            string? title = titleNode is not null
                ? HtmlEntity.DeEntitize(titleNode.GetAttributeValue("title", titleNode.InnerText)).Trim()
                : null;
            string? thumbnailUrl = GetImageUrl(item.SelectSingleNode(".//img[contains(@class,'thumbnail__image')]"))
                ?? FindNearbyImage(link);

            yield return new VideoMetadata
            {
                VideoId = videoId,
                Title = FirstNonEmptyOrNull(title) ?? DeriveTitleFromRumbleUrl(url) ?? "Unknown Title",
                Description = null,
                Url = url,
                ThumbnailUrl = thumbnailUrl,
                Duration = null,
                UploadDate = ParseUploadDate(item),
                ViewCount = ParseViewCount(item),
                LikeCount = null,
                ChannelId = string.Empty,
                ChannelName = ChannelNameFromAnchor(item.SelectSingleNode(".//a[contains(@class,'channel__link')]"))
                    ?? channelLabel,
                Platform = platformName,
                PlaylistId = playlistId
            };
        }
    }

    private static DateTime? ParseUploadDate(HtmlNode item)
    {
        string? datetime = item.SelectSingleNode(".//time[@datetime]")?.GetAttributeValue("datetime", null);
        return ParseIsoDate(datetime);
    }

    private static int? ParseViewCount(HtmlNode item)
    {
        string? views = item.SelectSingleNode(".//*[contains(@class,'videostream__views')][@data-views]")
            ?.GetAttributeValue("data-views", null);
        return int.TryParse(views, out int count) ? count : null;
    }

    private static IEnumerable<HtmlNode> EnumeratePlaylistAnchors(HtmlDocument doc)
    {
        var main = doc.DocumentNode.SelectSingleNode("//main");
        if (main is not null)
        {
            foreach (var anchor in main.SelectNodes(".//a[@href]") ?? Enumerable.Empty<HtmlNode>())
            {
                if (!IsInSidebar(anchor))
                {
                    yield return anchor;
                }
            }
        }

        foreach (var anchor in doc.DocumentNode.SelectNodes("//a[contains(@href, 'playlist_id=')]")
                     ?? Enumerable.Empty<HtmlNode>())
        {
            yield return anchor;
        }
    }

    private static bool IsPlaylistVideoHref(HtmlNode anchor, string absoluteUrl, string playlistId)
    {
        if (HrefBelongsToPlaylist(absoluteUrl, playlistId))
        {
            return true;
        }

        if (string.IsNullOrEmpty(ExtractRumbleVideoId(absoluteUrl)))
        {
            return false;
        }

        if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out var uri)
            || !uri.AbsolutePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Some playlist entries omit playlist_id from the href but still use the same
        // videostream card markup as the channel video grid (sidebar cards are excluded
        // earlier via EnumeratePlaylistAnchors / IsInSidebar).
        return IsVideostreamCardLink(anchor);
    }

    private static bool IsVideostreamCardLink(HtmlNode anchor)
    {
        if (anchor.GetAttributeValue("class", string.Empty)
            .Contains("videostream", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return anchor.Ancestors().Any(a =>
            a.GetAttributeValue("class", string.Empty)
                .Contains("videostream", StringComparison.OrdinalIgnoreCase));
    }

    private static void AddAnchorVideo(
        Dictionary<string, VideoMetadata> byId,
        HtmlNode anchor,
        string absolute,
        string playlistId,
        string channelLabel,
        string platformName)
    {
        string videoId = ExtractRumbleVideoId(absolute);
        if (string.IsNullOrEmpty(videoId))
        {
            return;
        }

        string url = StripQuery(absolute);
        var (strongTitle, _) = GetAnchorTitles(anchor);
        string? thumbnailUrl = GetImageUrl(anchor) ?? FindNearbyImage(anchor);

        MergeVideo(byId, new VideoMetadata
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
            ChannelName = channelLabel,
            Platform = platformName,
            PlaylistId = playlistId
        });
    }

    private static void MergeVideo(Dictionary<string, VideoMetadata> byId, VideoMetadata video)
    {
        if (!byId.TryGetValue(video.VideoId, out var existing))
        {
            byId[video.VideoId] = video;
            return;
        }

        if ((existing.Title == "Unknown Title" || string.IsNullOrWhiteSpace(existing.Title))
            && !string.IsNullOrWhiteSpace(video.Title)
            && video.Title != "Unknown Title")
        {
            existing.Title = video.Title;
        }

        existing.ThumbnailUrl ??= video.ThumbnailUrl;
        existing.Duration ??= video.Duration;
        existing.UploadDate ??= video.UploadDate;
        existing.ViewCount ??= video.ViewCount;
    }

    private static bool IsInSidebar(HtmlNode node) =>
        node.Ancestors().Any(a =>
            a.Name.Equals("aside", StringComparison.OrdinalIgnoreCase)
            || a.GetAttributeValue("class", string.Empty).Contains("sidebar", StringComparison.OrdinalIgnoreCase)
            || a.GetAttributeValue("class", string.Empty).Contains("recommend", StringComparison.OrdinalIgnoreCase));

    private static string DecodeHref(HtmlNode anchor) =>
        HtmlEntity.DeEntitize(anchor.GetAttributeValue("href", string.Empty));

    private static string ToAbsoluteUrl(string href) =>
        href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? href
            : "https://rumble.com" + href;

    private static string? ChannelNameFromAnchor(HtmlNode? anchor)
    {
        if (anchor is null)
        {
            return null;
        }

        string name = HtmlEntity.DeEntitize(anchor.InnerText).Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static bool HrefBelongsToPlaylist(string absoluteUrl, string playlistId)
    {
        if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        string query = uri.Query.TrimStart('?');
        if (string.IsNullOrEmpty(query))
        {
            return false;
        }

        foreach (string part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            string key = Uri.UnescapeDataString(part[..eq]);
            if (!key.Equals("playlist_id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = Uri.UnescapeDataString(part[(eq + 1)..]);
            return value.Equals(playlistId, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsVideoItem(JsonNode? item) =>
        item is not null && string.Equals(AsString(item["object_type"]), "video", StringComparison.OrdinalIgnoreCase);

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

    private static string? FirstNonEmptyOrNull(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
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

    private static string? NormalizeImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        url = url.Trim();
        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            return "https:" + url;
        }

        if (url.StartsWith("/", StringComparison.Ordinal))
        {
            return "https://rumble.com" + url;
        }

        return url;
    }

    private static bool IsUsableImageUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && !url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
        && (url.Contains(".rmbl.ws", StringComparison.OrdinalIgnoreCase)
            || url.Contains("1a-1791.com", StringComparison.OrdinalIgnoreCase)
            || url.Contains("rumble.com", StringComparison.OrdinalIgnoreCase));

    private static bool IsChannelCdnImage(string src) =>
        src.Contains("1a-1791.com", StringComparison.OrdinalIgnoreCase)
        || src.Contains(".rmbl.ws", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"rumble\.com/(?:embed/)?(v[0-9a-z]+)(?:-[^/?#]+\.html|/)", RegexOptions.IgnoreCase)]
    private static partial Regex RumbleVideoIdRegex();

    [GeneratedRegex(@"^v[0-9a-z]*[0-9][0-9a-z]*$", RegexOptions.IgnoreCase)]
    private static partial Regex RumbleVideoIdFormatRegex();

    [GeneratedRegex(@"rumble\.com/playlists/([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RumblePlaylistIdRegex();

    [GeneratedRegex(@"background-image\s*:\s*url\(\s*['""]?([^'"")]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BackgroundImageUrlRegex();
}
