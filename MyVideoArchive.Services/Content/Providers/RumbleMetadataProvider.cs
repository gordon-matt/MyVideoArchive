using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using MyVideoArchive.Models.Metadata;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
using YoutubeDLSharp.Options;

namespace MyVideoArchive.Services.Content.Providers;

/// <summary>
/// Rumble metadata provider. Channel video grids and playlist pages are scraped via
/// <see cref="RumblePageParser"/> because yt-dlp's Rumble channel extractor no longer sees
/// client-rendered listings. yt-dlp is kept only as a fallback for single-video metadata.
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

            string baseUrl = RumblePageParser.NormalizeChannelBaseUrl(channelUrl);
            string? html = await FetchHtmlAsync($"{baseUrl}/videos?page=1", cancellationToken);

            if (html is not null)
            {
                string? channelName = null;
                string? firstVideoThumbnail = null;

                var items = RumblePageParser.ExtractFirstGridItems(html);
                if (items is not null)
                {
                    foreach (var item in items)
                    {
                        var video = RumblePageParser.MapGridItemToVideo(item, channelId, "Unknown Channel", PlatformName);
                        if (video is null)
                        {
                            continue;
                        }

                        channelName = video.ChannelName;
                        firstVideoThumbnail = video.ThumbnailUrl;
                        break;
                    }
                }

                var (avatarUrl, bannerUrl) = RumblePageParser.ExtractHeaderImages(html);

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
                        Thumbnails = RumblePageParser.BuildThumbnailList(avatarUrl, bannerUrl, firstVideoThumbnail),
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

            string baseUrl = RumblePageParser.NormalizeChannelBaseUrl(channelUrl);
            var playlists = new Dictionary<string, PlaylistMetadata>(StringComparer.Ordinal);

            for (int page = 1; page <= MaxChannelPages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string? html = await FetchHtmlAsync($"{baseUrl}/playlists?page={page}", cancellationToken);
                if (html is null)
                {
                    break;
                }

                int addedThisPage = RumblePageParser.ParsePlaylistCards(html, channelId, PlatformName, playlists);
                if (addedThisPage == 0)
                {
                    break;
                }
            }

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Scraped {Count} playlists for Rumble channel: {Url}", playlists.Count, channelUrl);
            }

            await EnrichMissingPlaylistThumbnailsAsync(playlists.Values, cancellationToken);

            return playlists.Values.Where(p => !RumblePageParser.IsSystemPlaylist(p.Name)).ToList();
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

            var videos = await ScrapeChannelGridAsync(RumblePageParser.NormalizeChannelBaseUrl(channelUrl), channelId, cancellationToken);

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

    public async Task<PlaylistMetadata?> GetPlaylistMetadataAsync(string playlistUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Fetching Rumble playlist metadata for: {Url}", playlistUrl);
            }

            string playlistId = RumblePlaylistIdRegex().Match(playlistUrl).Groups[1].Value;

            string? html = await FetchHtmlAsync(RumblePageParser.StripQuery(playlistUrl), cancellationToken);
            if (html is null)
            {
                return null;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            string? title = RumblePageParser.GetMetaContent(doc, "og:title")
                ?? HtmlEntity.DeEntitize(doc.DocumentNode.SelectSingleNode("//h1")?.InnerText ?? string.Empty).Trim();
            string? description = RumblePageParser.GetMetaContent(doc, "og:description");
            string? thumbnailUrl = FirstNonEmptyOrNull(
                RumblePageParser.GetMetaContent(doc, "og:image"),
                RumblePageParser.GetMetaContent(doc, "twitter:image"));

            string channelName = RumblePageParser.ExtractPlaylistChannelName(doc) ?? string.Empty;
            var channelAnchor = doc.DocumentNode.SelectSingleNode("//a[starts-with(@href, '/c/') or starts-with(@href, '/user/')]");
            string channelId = channelAnchor is not null
                ? RumbleChannelIdRegex().Match("https://rumble.com" + channelAnchor.GetAttributeValue("href", string.Empty)).Groups[1].Value
                : string.Empty;

            if (string.IsNullOrEmpty(thumbnailUrl) && !string.IsNullOrEmpty(playlistId))
            {
                thumbnailUrl = RumblePageParser.ParsePlaylistVideos(html, playlistId, channelName, PlatformName)
                    .Select(v => v.ThumbnailUrl)
                    .FirstOrDefault(url => !string.IsNullOrEmpty(url));
            }

            return new PlaylistMetadata
            {
                PlaylistId = string.IsNullOrEmpty(playlistId) ? RumblePageParser.StripQuery(playlistUrl).Split('/').Last() : playlistId,
                Name = string.IsNullOrWhiteSpace(title) ? "Unknown Playlist" : title,
                Url = RumblePageParser.StripQuery(playlistUrl),
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

    public async Task<List<VideoMetadata>> GetPlaylistVideosAsync(string playlistUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Fetching Rumble playlist videos for: {Url}", playlistUrl);
            }

            string playlistId = FirstNonEmpty(
                RumblePlaylistIdRegex().Match(playlistUrl).Groups[1].Value,
                RumblePageParser.StripQuery(playlistUrl).Split('/').LastOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? string.Empty);

            if (string.IsNullOrEmpty(playlistId))
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Could not extract Rumble playlist id from {Url}", playlistUrl);
                }

                return [];
            }

            var metadata = await GetPlaylistMetadataAsync(playlistUrl, cancellationToken);
            string? channelName = metadata?.ChannelName;

            string baseUrl = RumblePageParser.StripQuery(playlistUrl);
            var videos = new List<VideoMetadata>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string? initialHtml = await FetchHtmlAsync(baseUrl, cancellationToken);
            if (initialHtml is null)
            {
                return [];
            }

            foreach (var video in RumblePageParser.ParsePlaylistVideos(initialHtml, playlistId, channelName, PlatformName))
            {
                if (seenIds.Add(video.VideoId))
                {
                    videos.Add(video);
                }
            }

            int? expectedCount = metadata is not null ? TryParseExpectedVideoCount(initialHtml) : null;

            if (RumblePageParser.TryParsePlaylistHtmxPagination(initialHtml, out var htmx))
            {
                int maxPages = expectedCount.HasValue && htmx.PageSize > 0
                    ? (int)Math.Ceiling(expectedCount.Value / (double)htmx.PageSize)
                    : MaxChannelPages;

                for (int pageNum = 2; pageNum <= maxPages; pageNum++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string? fragment = await FetchPlaylistHtmxPageAsync(htmx, pageNum, baseUrl, cancellationToken);
                    if (string.IsNullOrWhiteSpace(fragment))
                    {
                        break;
                    }

                    int addedThisPage = 0;
                    foreach (var video in RumblePageParser.ParsePlaylistVideos(fragment, playlistId, channelName, PlatformName))
                    {
                        if (!seenIds.Add(video.VideoId))
                        {
                            continue;
                        }

                        videos.Add(video);
                        addedThisPage++;
                    }

                    if (addedThisPage == 0)
                    {
                        break;
                    }
                }
            }
            else
            {
                for (int page = 2; page <= MaxChannelPages; page++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string? html = await FetchHtmlAsync($"{baseUrl}?page={page}", cancellationToken);
                    if (html is null)
                    {
                        break;
                    }

                    int addedThisPage = 0;
                    foreach (var video in RumblePageParser.ParsePlaylistVideos(html, playlistId, channelName, PlatformName))
                    {
                        if (!seenIds.Add(video.VideoId))
                        {
                            continue;
                        }

                        videos.Add(video);
                        addedThisPage++;
                    }

                    if (addedThisPage == 0)
                    {
                        break;
                    }
                }
            }

            await EnrichMissingVideoThumbnailsViaOEmbedAsync(videos, cancellationToken);

            if (expectedCount.HasValue && videos.Count < expectedCount.Value && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    "Rumble playlist {PlaylistId} page advertises {Expected} videos but only {Actual} were scraped from {Url}",
                    playlistId, expectedCount.Value, videos.Count, playlistUrl);
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
                : RumblePageParser.ExtractRumbleVideoId(data.WebpageUrl ?? videoUrl);

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

            var items = RumblePageParser.ExtractFirstGridItems(html);
            if (items is null)
            {
                break;
            }

            int addedThisPage = 0;
            foreach (var item in items)
            {
                var video = RumblePageParser.MapGridItemToVideo(item, channelId, channelName ?? "Unknown Channel", PlatformName);
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

    private async Task<string?> FetchPlaylistHtmxPageAsync(
        RumblePageParser.RumblePlaylistHtmxPagination config,
        int pageNum,
        string refererUrl,
        CancellationToken cancellationToken)
    {
        string url = BuildPlaylistHtmxUrl(config, pageNum);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("HX-Request", "true");
            request.Headers.TryAddWithoutValidation("HX-Current-URL", refererUrl);
            request.Headers.Referrer = new Uri(refererUrl);
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug(
                        "Rumble playlist HTMX fetch failed for playlist {PlaylistId} page {PageNum}: {StatusCode}",
                        config.PlaylistId, pageNum, response.StatusCode);
                }

                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(ex,
                    "Error fetching Rumble playlist HTMX page {PageNum} for playlist {PlaylistId}",
                    pageNum, config.PlaylistId);
            }

            return null;
        }
    }

    private static string BuildPlaylistHtmxUrl(RumblePageParser.RumblePlaylistHtmxPagination config, int pageNum)
    {
        var query = new[]
        {
            $"playlist_id={Uri.EscapeDataString(config.PlaylistId)}",
            $"shuffle_param={Uri.EscapeDataString(config.ShuffleParam)}",
            $"page_size={config.PageSize}",
            $"pagination={config.Pagination}",
            $"page_num={pageNum}",
        };

        return "https://rumble.com/-playlists/htmx/get-playlist-details?" + string.Join('&', query);
    }

    private async Task EnrichMissingPlaylistThumbnailsAsync(
        IEnumerable<PlaylistMetadata> playlists,
        CancellationToken cancellationToken)
    {
        foreach (var playlist in playlists)
        {
            if (!string.IsNullOrEmpty(playlist.ThumbnailUrl))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            string? html = await FetchHtmlAsync(playlist.Url, cancellationToken);
            if (html is null)
            {
                continue;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            playlist.ThumbnailUrl = FirstNonEmptyOrNull(
                RumblePageParser.GetMetaContent(doc, "og:image"),
                RumblePageParser.GetMetaContent(doc, "twitter:image"),
                RumblePageParser.ParsePlaylistVideos(html, playlist.PlaylistId, playlist.ChannelName, PlatformName)
                    .Select(v => v.ThumbnailUrl)
                    .FirstOrDefault(url => !string.IsNullOrEmpty(url)));
        }
    }

    private async Task EnrichMissingVideoThumbnailsViaOEmbedAsync(
        List<VideoMetadata> videos,
        CancellationToken cancellationToken)
    {
        foreach (var video in videos)
        {
            if (!string.IsNullOrEmpty(video.ThumbnailUrl))
            {
                continue;
            }

            var oembed = await GetOEmbedAsync(video.Url, cancellationToken);
            video.ThumbnailUrl = oembed?.ThumbnailUrl;
        }
    }

    private static int? TryParseExpectedVideoCount(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return null;
        }

        var match = PlaylistVideoCountRegex().Match(html);
        return match.Success && int.TryParse(match.Groups[1].Value, out int count) ? count : null;
    }

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

        var results = new List<VideoMetadata>(entries.Length);
        foreach (var x in entries)
        {
            if (x is null)
            {
                continue;
            }

            string url = x.WebpageUrl ?? x.Url ?? string.Empty;
            string videoId = !string.IsNullOrEmpty(x.ID) ? x.ID : RumblePageParser.ExtractRumbleVideoId(url);
            if (string.IsNullOrEmpty(videoId))
            {
                continue;
            }

            var oembed = string.IsNullOrEmpty(url) ? null : await GetOEmbedAsync(url, cancellationToken);

            results.Add(new VideoMetadata
            {
                VideoId = videoId,
                Title = x.Title ?? oembed?.Title ?? RumblePageParser.DeriveTitleFromRumbleUrl(url) ?? "Unknown Title",
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

    [GeneratedRegex(@"rumble\.com/(?:c|user)/([^/?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RumbleChannelIdRegex();

    [GeneratedRegex(@"rumble\.com", RegexOptions.IgnoreCase)]
    private static partial Regex RumbleUrlRegex();

    [GeneratedRegex(@"rumble\.com/playlists/([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RumblePlaylistIdRegex();

    [GeneratedRegex(@"(\d+)\s+videos", RegexOptions.IgnoreCase)]
    private static partial Regex PlaylistVideoCountRegex();

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

    private static string? FirstNonEmptyOrNull(params string?[] values)
    {
        string result = FirstNonEmpty(values);
        return string.IsNullOrEmpty(result) ? null : result;
    }

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
