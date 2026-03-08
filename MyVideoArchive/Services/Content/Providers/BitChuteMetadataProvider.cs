using System.Text.RegularExpressions;
using HtmlAgilityPack;
using MyVideoArchive.Models.Metadata;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
using YoutubeDLSharp.Options;

namespace MyVideoArchive.Services.Content.Providers;

/// <summary>
/// BitChute implementation of metadata provider using yt-dlp.
/// Supports channel URLs (bitchute.com/channel/{id}) and playlist URLs (bitchute.com/playlist/{id}).
/// </summary>
public partial class BitChuteMetadataProvider : IVideoMetadataProvider
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger<BitChuteMetadataProvider> logger;
    private readonly HttpClient thumbnailHttpClient;
    private readonly YoutubeDL ytdl;

    public BitChuteMetadataProvider(
        ILogger<BitChuteMetadataProvider> logger,
        YoutubeDL ytdl,
        IHttpClientFactory httpClientFactory)
    {
        this.logger = logger;
        this.ytdl = ytdl;
        this.httpClientFactory = httpClientFactory;
        thumbnailHttpClient = httpClientFactory.CreateClient("thumbnails");
    }

    public string PlatformName => "BitChute";

    public string BuildChannelUrl(string channelId) => $"https://www.bitchute.com/channel/{channelId}/";

    public bool CanHandle(string url) => BitChuteUrlRegex().IsMatch(url);

    public async Task<ChannelMetadata?> GetChannelMetadataAsync(string channelUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Fetching BitChute channel metadata for: {Url}", channelUrl);
            }

            // Fetch a flat listing to get channel info without downloading all video details
            var options = new OptionSet
            {
                FlatPlaylist = true,
                DumpSingleJson = true,
                PlaylistEnd = 1
            };

            var result = await ytdl.RunVideoDataFetch(channelUrl, overrideOptions: options);

            if (!result.Success || result.Data is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed to fetch BitChute channel metadata for {Url}: {Error}", channelUrl, string.Join(", ", result.ErrorOutput));
                }

                return null;
            }

            var data = result.Data;

            string channelId = data.ChannelID
                ?? BitChuteChannelIdRegex().Match(channelUrl).Groups[1].Value
                ?? string.Empty;

            var thumbnailInfos = MapThumbnails(data.Thumbnails);
            return new ChannelMetadata
            {
                ChannelId = channelId,
                Name = data.Channel ?? data.Uploader ?? data.Title ?? "Unknown Channel",
                Url = data.WebpageUrl ?? channelUrl,
                Description = data.Description,
                BannerUrl = GetBestThumbnailFromYtDlp(data.Thumbnails),
                Thumbnails = thumbnailInfos,
                SubscriberCount = data.ChannelFollowerCount is not null ? (int?)data.ChannelFollowerCount : null,
                Platform = PlatformName
            };
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error fetching BitChute channel metadata for {Url}", channelUrl);
            }
            return null;
        }
    }

    /// <summary>
    /// Scrapes BitChute for channel playlists: fetches the channel API HTML (which contains the profile link;
    /// the www channel page is an SPA with no server-rendered profile link), then the profile page for playlists.
    /// yt-dlp does not return playlist data for BitChute channels.
    /// </summary>
    public async Task<List<PlaylistMetadata>> GetChannelPlaylistsAsync(string channelUrl, CancellationToken cancellationToken = default)
    {
        const string baseUrl = "https://www.bitchute.com";
        const string apiBaseUrl = "https://api.bitchute.com";
        var channelIdMatch = BitChuteChannelIdRegex().Match(channelUrl);
        string channelId = channelIdMatch.Success ? channelIdMatch.Groups[1].Value : string.Empty;

        if (string.IsNullOrEmpty(channelId))
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("Could not extract channel ID from BitChute URL: {Url}", channelUrl);
            }
            return [];
        }

        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Scraping BitChute channel playlists for: {Url}", channelUrl);
            }

            using var httpClient = httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            // 1. Fetch channel API page (returns HTML with profile link; www channel page is SPA and has no profile link in initial HTML)
            string apiChannelUrl = $"{apiBaseUrl}/channel/{channelId}/";
            string? channelHtml = null;
            using (var channelResponse = await httpClient.GetAsync(apiChannelUrl, cancellationToken))
            {
                if (channelResponse.IsSuccessStatusCode)
                {
                    channelHtml = await channelResponse.Content.ReadAsStringAsync(cancellationToken);
                }
            }

            if (string.IsNullOrWhiteSpace(channelHtml))
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed to fetch BitChute channel API page: {Url}", apiChannelUrl);
                }
                return [];
            }

            var channelDoc = new HtmlDocument();
            channelDoc.LoadHtml(channelHtml);
            string? profilePath = GetProfilePathFromChannelDocument(channelDoc);
            if (string.IsNullOrWhiteSpace(profilePath))
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("No profile link found in BitChute channel API response: {Url}", apiChannelUrl);
                }
                return [];
            }

            string channelName = GetChannelNameFromChannelApiDocument(channelDoc) ?? "Unknown Channel";

            // 2. Fetch profile page from API (www profile page is SPA with no playlists in initial HTML)
            string apiProfileUrl = profilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? profilePath.Replace(baseUrl, apiBaseUrl, StringComparison.OrdinalIgnoreCase)
                : apiBaseUrl + (profilePath.StartsWith("/", StringComparison.Ordinal) ? profilePath : "/" + profilePath);

            using var profileResponse = await httpClient.GetAsync(apiProfileUrl, cancellationToken);
            if (!profileResponse.IsSuccessStatusCode)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed to fetch BitChute profile API page {Url}: {StatusCode}", apiProfileUrl, profileResponse.StatusCode);
                }
                return [];
            }

            string profileHtml = await profileResponse.Content.ReadAsStringAsync(cancellationToken);
            var playlists = ParsePlaylistsFromProfileHtml(profileHtml, baseUrl, channelId, channelName);

            if (logger.IsEnabled(LogLevel.Information) && playlists.Count > 0)
            {
                logger.LogInformation("Scraped {Count} playlists for BitChute channel: {Url}", playlists.Count, channelUrl);
            }

            return playlists;
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error scraping BitChute channel playlists for {Url}", channelUrl);
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
                logger.LogInformation("Fetching BitChute channel videos for: {Url}", channelUrl);
            }

            var options = new OptionSet
            {
                FlatPlaylist = true,
                DumpSingleJson = true
            };

            var result = await ytdl.RunVideoDataFetch(channelUrl, overrideOptions: options);

            if (!result.Success || result.Data is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed to fetch BitChute channel videos for {Url}: {Error}", channelUrl, string.Join(", ", result.ErrorOutput));
                }

                return [];
            }

            string channelId = result.Data.ChannelID
                ?? BitChuteChannelIdRegex().Match(channelUrl).Groups[1].Value
                ?? string.Empty;

            string channelName = result.Data.Channel ?? result.Data.Uploader ?? "Unknown Channel";

            return await MapEntriesToVideoMetadataAsync(result.Data.Entries, channelId, channelName, cancellationToken);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error fetching BitChute channel videos for {Url}", channelUrl);
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
                logger.LogInformation("Fetching BitChute playlist metadata for: {Url}", playlistUrl);
            }

            var options = new OptionSet
            {
                FlatPlaylist = true,
                DumpSingleJson = true
            };

            var result = await ytdl.RunVideoDataFetch(playlistUrl, overrideOptions: options);

            if (!result.Success || result.Data is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed to fetch BitChute playlist metadata for {Url}: {Error}", playlistUrl, string.Join(", ", result.ErrorOutput));
                }

                return null;
            }

            var data = result.Data;

            string playlistId = data.ID
                ?? BitChutePlaylistIdRegex().Match(playlistUrl).Groups[1].Value
                ?? string.Empty;

            return new PlaylistMetadata
            {
                PlaylistId = playlistId,
                Name = data.Title ?? "Unknown Playlist",
                Url = data.WebpageUrl ?? playlistUrl,
                ThumbnailUrl = GetBestThumbnailFromYtDlp(data.Thumbnails),
                Description = data.Description,
                ChannelId = data.ChannelID ?? data.UploaderID ?? string.Empty,
                ChannelName = data.Channel ?? data.Uploader ?? "Unknown Channel",
                Platform = PlatformName,
                VideoIds = []
            };
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error fetching BitChute playlist metadata for {Url}", playlistUrl);
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
                logger.LogInformation("Fetching BitChute playlist videos for: {Url}", playlistUrl);
            }

            var options = new OptionSet
            {
                FlatPlaylist = true,
                DumpSingleJson = true
            };

            var result = await ytdl.RunVideoDataFetch(playlistUrl, overrideOptions: options);

            if (!result.Success || result.Data is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed to fetch BitChute playlist videos for {Url}: {Error}", playlistUrl, string.Join(", ", result.ErrorOutput));
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
                logger.LogError(ex, "Error fetching BitChute playlist videos for {Url}", playlistUrl);
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
                logger.LogInformation("Fetching BitChute video metadata for: {Url}", videoUrl);
            }

            var result = await ytdl.RunVideoDataFetch(videoUrl);

            if (!result.Success || result.Data is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed to fetch BitChute video metadata for {Url}: {Error}", videoUrl, string.Join(", ", result.ErrorOutput));
                }

                return null;
            }

            var data = result.Data;

            string? thumbnailUrl = await ResolveThumbnailUrlAsync(
                thumbnails: data.Thumbnails,
                channelId: data.ChannelID,
                videoId: data.ID,
                cancellationToken);

            return new VideoMetadata
            {
                VideoId = data.ID ?? string.Empty,
                Title = data.Title ?? "Unknown Title",
                Description = data.Description,
                Url = data.WebpageUrl ?? videoUrl,
                ThumbnailUrl = thumbnailUrl,
                Duration = data.Duration.HasValue ? TimeSpan.FromSeconds(data.Duration.Value) : null,
                UploadDate = data.UploadDate.AsUtc(),
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
                logger.LogError(ex, "Error fetching BitChute video metadata for {Url}", videoUrl);
            }
            return null;
        }
    }

    [GeneratedRegex(@"bitchute\.com/channel/([^/?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BitChuteChannelIdRegex();

    [GeneratedRegex(@"bitchute\.com/playlist/([^/?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BitChutePlaylistIdRegex();

    [GeneratedRegex(@"bitchute\.com", RegexOptions.IgnoreCase)]
    private static partial Regex BitChuteUrlRegex();

    private static string? GetBestThumbnailFromYtDlp(ThumbnailData[]? thumbnails)
    {
        if (thumbnails.IsNullOrEmpty())
        {
            return null;
        }

        var best = thumbnails!
            .Where(t => !string.IsNullOrEmpty(t.Url))
            .OrderByDescending(t => (t.Width ?? 0) * (t.Height ?? 0))
            .FirstOrDefault();

        return best?.Url;
    }

    /// <summary>
    /// Gets channel display name from the channel API document (e.g. from &lt;title&gt; "Red Pill Central").
    /// Falls back to the profile link text if title is missing.
    /// </summary>
    private static string? GetChannelNameFromChannelApiDocument(HtmlDocument doc)
    {
        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        string? name = titleNode?.InnerText?.Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }
        var profileLink = doc.DocumentNode.SelectSingleNode("//a[starts-with(@href, '/profile/')]")
            ?? doc.DocumentNode.SelectSingleNode("//a[contains(@href, '/profile/')]");
        name = profileLink?.InnerText?.Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>
    /// Gets the playlist thumbnail URL from the playlist link node.
    /// API uses img with data-src like https://static-3.bitchute.com/live/playlist_images/{playlistId}/{hash}_large.jpg.
    /// Prefers _large, then _medium, then any playlist_images URL.
    /// </summary>
    private static string? GetPlaylistThumbnailFromLink(HtmlNode link)
    {
        var imgs = link.SelectNodes(".//img[contains(@data-src, 'playlist_images')]");
        if (imgs is null || imgs.Count == 0)
        {
            return null;
        }
        string? large = null;
        string? medium = null;
        string? any = null;
        foreach (var img in imgs)
        {
            string? dataSrc = img.GetAttributeValue("data-src", string.Empty);
            if (string.IsNullOrWhiteSpace(dataSrc))
            {
                continue;
            }
            any ??= dataSrc.Trim();
            if (dataSrc.Contains("_large.", StringComparison.OrdinalIgnoreCase))
            {
                large = dataSrc.Trim();
            }
            else if (dataSrc.Contains("_medium.", StringComparison.OrdinalIgnoreCase))
            {
                medium ??= dataSrc.Trim();
            }
        }
        return large ?? medium ?? any;
    }

    /// <summary>
    /// Finds the profile link in the channel API HTML. Selector: a[href^="/profile/"] or a[contains(@href, '/profile/')].
    /// The www channel page is an SPA and does not include this in initial HTML; the API does.
    /// </summary>
    private static string? GetProfilePathFromChannelDocument(HtmlDocument doc)
    {
        var profileLink = doc.DocumentNode.SelectSingleNode("//a[starts-with(@href, '/profile/')]")
            ?? doc.DocumentNode.SelectSingleNode("//a[contains(@href, '/profile/')]");
        string? href = profileLink?.GetAttributeValue("href", string.Empty);
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }
        href = href.Trim();
        // If API returns full URL (e.g. https://api.bitchute.com/profile/xxx/), extract path for www
        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase) && href.Contains("/profile/", StringComparison.OrdinalIgnoreCase))
        {
            int profileIdx = href.IndexOf("/profile/", StringComparison.OrdinalIgnoreCase);
            href = href[profileIdx..];
        }
        return href;
    }

    private static List<ThumbnailInfo> MapThumbnails(ThumbnailData[]? thumbnails) => thumbnails.IsNullOrEmpty()
            ? []
            : thumbnails!
                .Where(t => !string.IsNullOrEmpty(t.Url))
                .Select(t => new ThumbnailInfo(t.ID, t.Url!, t.Width, t.Height, t.Preference))
                .ToList();

    /// <summary>
    /// Parses playlist entries from the profile page HTML (API format: playlist-card with a[href^="/playlist/"] and div.title).
    /// </summary>
    private static List<PlaylistMetadata> ParsePlaylistsFromProfileHtml(string profileHtml, string baseUrl, string channelId, string channelName)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(profileHtml);

        // API profile: <a href="/playlist/uXKT8AVECeuD/" class="spa"> ... <div class="title">The Great Flood</div> </a>
        var playlistLinks = doc.DocumentNode.SelectNodes("//a[starts-with(@href, '/playlist/')]")
            ?? doc.DocumentNode.SelectNodes("//a[contains(@href, '/playlist/')]");
        if (playlistLinks is null || playlistLinks.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<PlaylistMetadata>();

        foreach (var link in playlistLinks)
        {
            string href = link.GetAttributeValue("href", string.Empty);
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            // Extract playlist ID: /playlist/uXKT8AVECeuD/ -> uXKT8AVECeuD
            string path = href.TrimEnd('/');
            int lastSlash = path.LastIndexOf('/');
            string playlistId = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
            if (string.IsNullOrWhiteSpace(playlistId) || !seen.Add(playlistId))
            {
                continue;
            }

            // API uses div.title; SPA (www) uses div.text-h6 / q-item__label
            var titleNode = link.SelectSingleNode(".//div[contains(@class, 'title')]")
                ?? link.SelectSingleNode(".//*[contains(@class, 'text-h6')]")
                ?? link.SelectSingleNode(".//*[contains(@class, 'q-item__label')]");
            string name = titleNode?.InnerText?.Trim() ?? "Unknown Playlist";
            if (!string.IsNullOrEmpty(name))
            {
                name = System.Net.WebUtility.HtmlDecode(name);
            }

            // Thumbnail: API uses lazy-loaded img with data-src like .../playlist_images/{id}/{hash}_large.jpg
            string? thumbnailUrl = GetPlaylistThumbnailFromLink(link);

            string playlistUrl = href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? href
                : baseUrl + (href.StartsWith("/", StringComparison.Ordinal) ? href : "/" + href);

            list.Add(new PlaylistMetadata
            {
                PlaylistId = playlistId,
                Name = name,
                Url = playlistUrl,
                Description = null,
                ThumbnailUrl = thumbnailUrl,
                ChannelId = channelId,
                ChannelName = channelName,
                Platform = "BitChute",
                VideoIds = []
            });
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

        // Sequential by design (polite to BitChute + avoids spinning up many ffmpeg processes)
        var results = new List<VideoMetadata>(entries.Length);
        foreach (var x in entries)
        {
            string videoId = x.ID ?? string.Empty;

            string? thumbnailUrl = await ResolveThumbnailUrlAsync(
                thumbnails: x.Thumbnails,
                channelId: channelId,
                videoId: videoId,
                cancellationToken);

            results.Add(new VideoMetadata
            {
                VideoId = videoId,
                Title = x.Title ?? "Unknown Title",
                Description = x.Description,
                Url = x.WebpageUrl ?? x.Url ?? (string.IsNullOrEmpty(videoId)
                    ? string.Empty
                    : $"https://www.bitchute.com/video/{videoId}/"),
                ThumbnailUrl = thumbnailUrl,
                Duration = x.Duration.HasValue ? TimeSpan.FromSeconds(x.Duration.Value) : null,
                UploadDate = x.UploadDate.AsUtc(),
                ViewCount = x.ViewCount.HasValue ? (int?)x.ViewCount : null,
                LikeCount = x.LikeCount.HasValue ? (int?)x.LikeCount : null,
                ChannelId = channelId,
                ChannelName = channelName,
                Platform = PlatformName,
                PlaylistId = null
            });
        }

        return results;
    }

    private async Task<string?> ResolveThumbnailUrlAsync(
        ThumbnailData[]? thumbnails,
        string? channelId,
        string? videoId,
        CancellationToken cancellationToken)
    {
        string? fromYtDlp = GetBestThumbnailFromYtDlp(thumbnails);
        if (!string.IsNullOrEmpty(fromYtDlp))
        {
            return fromYtDlp;
        }

        if (!string.IsNullOrWhiteSpace(channelId) && !string.IsNullOrWhiteSpace(videoId))
        {
            string? cover = await TryGetStaticCoverImageUrlAsync(channelId, videoId, cancellationToken);
            if (!string.IsNullOrEmpty(cover))
            {
                return cover;
            }
        }

        return null;
    }

    private async Task<string?> TryGetStaticCoverImageUrlAsync(string channelId, string videoId, CancellationToken cancellationToken)
    {
        // Prefer higher-res first
        string[] candidates =
        [
            $"https://static-3.bitchute.com/live/cover_images/{channelId}/{videoId}_1280x720.jpg",
            $"https://static-3.bitchute.com/live/cover_images/{channelId}/{videoId}_640x360.jpg",
        ];

        foreach (string url in candidates)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await thumbnailHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return url;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Ignore and fall back; we'll try the next candidate or generate
            }
        }

        return null;
    }
}