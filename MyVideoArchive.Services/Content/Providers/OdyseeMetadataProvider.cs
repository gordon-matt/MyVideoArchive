using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MyVideoArchive.Models.Metadata;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
using YoutubeDLSharp.Options;

namespace MyVideoArchive.Services.Content.Providers;

/// <summary>
/// Odysee implementation of metadata provider using yt-dlp (LBRY extractor) for video/playlist
/// listing, and Odysee's underlying LBRY JSON-RPC API directly for channel metadata and playlist
/// (collection) discovery — yt-dlp's channel extractor never exposes a channel avatar/banner, and
/// has no way to enumerate a channel's playlists at all.
/// Supports channel URLs (odysee.com/@Handle:id) and single video URLs.
/// </summary>
public partial class OdyseeMetadataProvider : IVideoMetadataProvider
{
    // Documented as the current SDK proxy host for the Odysee frontend. yt-dlp's LBRY extractor
    // still uses the older api.lbry.tv host, which proxies to the same backend.
    private const string ApiProxyUrl = "https://api.na-backend.odysee.com/api/v1/proxy";
    private const int CollectionsPageSize = 50;

    private readonly ILogger<OdyseeMetadataProvider> logger;
    private readonly IConfiguration configuration;
    private readonly YoutubeDL ytdl;
    private readonly HttpClient httpClient;

    public OdyseeMetadataProvider(
        ILogger<OdyseeMetadataProvider> logger,
        IConfiguration configuration,
        YoutubeDL ytdl,
        IHttpClientFactory httpClientFactory)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.ytdl = ytdl;
        httpClient = httpClientFactory.CreateClient();
    }

    public string PlatformName => "Odysee";

    public string BuildChannelUrl(string channelId)
    {
        if (channelId.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return channelId;
        }

        string handle = channelId.StartsWith('@') ? channelId : "@" + channelId;
        return $"https://odysee.com/{handle}";
    }

    public bool CanHandle(string url) => OdyseeUrlRegex().IsMatch(url);

    public async Task<ChannelMetadata?> GetChannelMetadataAsync(string channelUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Fetching Odysee channel metadata for: {Url}", channelUrl);
            }

            // Primary path: resolve the channel claim directly via the LBRY API. This works even
            // for channels with zero videos (yt-dlp's flat listing below requires at least one
            // video to return anything) and is the only way to get a real avatar/banner image,
            // since yt-dlp's channel extractor never maps thumbnail/cover for channels.
            var resolved = await ResolveChannelClaimAsync(channelUrl, cancellationToken);
            if (resolved is not null)
            {
                string resolvedChannelId = FirstNonEmpty(
                    OdyseeChannelIdRegex().Match(channelUrl).Groups[1].Value,
                    resolved.ClaimId);

                return new ChannelMetadata
                {
                    ChannelId = resolvedChannelId,
                    Name = string.IsNullOrEmpty(resolved.Name) ? "Unknown Channel" : resolved.Name,
                    Url = channelUrl,
                    Description = resolved.Description,
                    BannerUrl = resolved.BannerUrl ?? resolved.AvatarUrl,
                    AvatarUrl = resolved.AvatarUrl,
                    Thumbnails = BuildThumbnailList(resolved.AvatarUrl, resolved.BannerUrl),
                    SubscriberCount = null,
                    Platform = PlatformName
                };
            }

            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("Direct LBRY resolve failed for {Url}; falling back to yt-dlp", channelUrl);
            }

            // Fallback: yt-dlp's flat channel fetch (only works if the channel has ≥1 video).
            var options = new OptionSet
            {
                FlatPlaylist = true,
                DumpSingleJson = true,
                PlaylistEnd = 1
            };

            OdyseeRateLimitOptions.Apply(options, configuration, logger);

            var result = await ytdl.RunVideoDataFetch(channelUrl, overrideOptions: options, ct: cancellationToken);

            if (!result.Success || result.Data is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed to fetch Odysee channel metadata for {Url}: {Error}", channelUrl, string.Join(", ", result.ErrorOutput));
                }

                return null;
            }

            var data = result.Data;

            string channelId = data.ChannelID
                ?? data.UploaderID
                ?? OdyseeChannelIdRegex().Match(channelUrl).Groups[1].Value
                ?? string.Empty;

            return new ChannelMetadata
            {
                ChannelId = channelId,
                Name = data.Channel ?? data.Uploader ?? data.Title ?? "Unknown Channel",
                Url = data.WebpageUrl ?? channelUrl,
                Description = data.Description,
                BannerUrl = PickThumbnail(data.Thumbnails, data.Thumbnail),
                AvatarUrl = PickThumbnail(data.Thumbnails, data.Thumbnail),
                Thumbnails = MapThumbnails(data.Thumbnails),
                SubscriberCount = data.ChannelFollowerCount is not null ? (int?)data.ChannelFollowerCount : null,
                Platform = PlatformName
            };
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error fetching Odysee channel metadata for {Url}", channelUrl);
            }
            return null;
        }
    }

    /// <summary>
    /// Lists a channel's playlists (LBRY "collection" claims). yt-dlp has no extractor for this,
    /// so we call the LBRY <c>claim_search</c> API directly — analogous to how
    /// <see cref="BitChuteMetadataProvider"/> scrapes HTML for the same purpose.
    /// </summary>
    public async Task<List<PlaylistMetadata>> GetChannelPlaylistsAsync(string channelUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            var resolved = await ResolveChannelClaimAsync(channelUrl, cancellationToken);
            if (resolved is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Could not resolve Odysee channel claim for playlists: {Url}", channelUrl);
                }
                return [];
            }

            string storedChannelId = FirstNonEmpty(
                OdyseeChannelIdRegex().Match(channelUrl).Groups[1].Value,
                resolved.ClaimId);

            var playlists = new List<PlaylistMetadata>();

            for (int page = 1; ; page++)
            {
                var parameters = new JsonObject
                {
                    ["channel_ids"] = new JsonArray(resolved.ClaimId),
                    ["claim_type"] = "collection",
                    ["page"] = page,
                    ["page_size"] = CollectionsPageSize,
                    ["no_totals"] = true
                };

                var result = await CallApiProxyAsync("claim_search", parameters, cancellationToken);
                var items = result?["items"]?.AsArray();
                if (items is null || items.Count == 0)
                {
                    break;
                }

                foreach (var item in items)
                {
                    string? collectionClaimId = AsString(item?["claim_id"]);
                    if (string.IsNullOrEmpty(collectionClaimId))
                    {
                        continue;
                    }

                    var value = item?["value"];
                    string name = AsString(value?["title"]) ?? AsString(item?["name"]) ?? "Unknown Playlist";

                    playlists.Add(new PlaylistMetadata
                    {
                        PlaylistId = collectionClaimId,
                        Name = name,
                        Url = $"https://odysee.com/$/playlist/{collectionClaimId}",
                        Description = AsString(value?["description"]),
                        ThumbnailUrl = AsString(value?["thumbnail"]?["url"]),
                        ChannelId = storedChannelId,
                        ChannelName = resolved.Name,
                        Platform = PlatformName
                    });
                }

                if (items.Count < CollectionsPageSize)
                {
                    break;
                }
            }

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Found {Count} playlists for Odysee channel {Url}", playlists.Count, channelUrl);
            }

            return playlists;
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error fetching Odysee channel playlists for {Url}", channelUrl);
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
                logger.LogInformation("Fetching Odysee channel videos for: {Url}", channelUrl);
            }

            var options = new OptionSet
            {
                FlatPlaylist = true,
                DumpSingleJson = true
            };

            OdyseeRateLimitOptions.Apply(options, configuration, logger);

            var result = await ytdl.RunVideoDataFetch(channelUrl, overrideOptions: options, ct: cancellationToken);

            if (!result.Success || result.Data is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed to fetch Odysee channel videos for {Url}: {Error}", channelUrl, string.Join(", ", result.ErrorOutput));
                }

                return [];
            }

            string channelId = result.Data.ChannelID
                ?? result.Data.UploaderID
                ?? OdyseeChannelIdRegex().Match(channelUrl).Groups[1].Value
                ?? string.Empty;

            string channelName = result.Data.Channel ?? result.Data.Uploader ?? "Unknown Channel";

            return MapEntriesToVideoMetadata(result.Data.Entries, channelId, channelName);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error fetching Odysee channel videos for {Url}", channelUrl);
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
                logger.LogInformation("Fetching Odysee playlist metadata for: {Url}", playlistUrl);
            }

            var options = new OptionSet
            {
                FlatPlaylist = true,
                DumpSingleJson = true
            };

            OdyseeRateLimitOptions.Apply(options, configuration, logger);

            var result = await ytdl.RunVideoDataFetch(playlistUrl, overrideOptions: options, ct: cancellationToken);

            if (!result.Success || result.Data is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed to fetch Odysee playlist metadata for {Url}: {Error}", playlistUrl, string.Join(", ", result.ErrorOutput));
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
                logger.LogError(ex, "Error fetching Odysee playlist metadata for {Url}", playlistUrl);
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
                logger.LogInformation("Fetching Odysee playlist videos for: {Url}", playlistUrl);
            }

            var options = new OptionSet
            {
                FlatPlaylist = true,
                DumpSingleJson = true
            };

            OdyseeRateLimitOptions.Apply(options, configuration, logger);

            var result = await ytdl.RunVideoDataFetch(playlistUrl, overrideOptions: options, ct: cancellationToken);

            if (!result.Success || result.Data is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed to fetch Odysee playlist videos for {Url}: {Error}", playlistUrl, string.Join(", ", result.ErrorOutput));
                }

                return [];
            }

            string channelId = result.Data.ChannelID ?? result.Data.UploaderID ?? string.Empty;
            string channelName = result.Data.Channel ?? result.Data.Uploader ?? "Unknown Channel";

            return MapEntriesToVideoMetadata(result.Data.Entries, channelId, channelName);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error fetching Odysee playlist videos for {Url}", playlistUrl);
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
                logger.LogInformation("Fetching Odysee video metadata for: {Url}", videoUrl);
            }

            var options = new OptionSet();
            OdyseeRateLimitOptions.Apply(options, configuration, logger);

            var result = await ytdl.RunVideoDataFetch(videoUrl, overrideOptions: options, ct: cancellationToken);

            if (!result.Success || result.Data is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed to fetch Odysee video metadata for {Url}: {Error}", videoUrl, string.Join(", ", result.ErrorOutput));
                }

                return null;
            }

            var data = result.Data;

            return new VideoMetadata
            {
                VideoId = data.ID ?? string.Empty,
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
                logger.LogError(ex, "Error fetching Odysee video metadata for {Url}", videoUrl);
            }
            return null;
        }
    }

    [GeneratedRegex(@"odysee\.com/(@[^/?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex OdyseeChannelIdRegex();

    [GeneratedRegex(@"odysee\.com", RegexOptions.IgnoreCase)]
    private static partial Regex OdyseeUrlRegex();

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

    /// <summary>
    /// Safely reads a string value out of a <see cref="JsonNode"/> without throwing when the
    /// node is missing or not a string (the LBRY API's shape varies a lot between claim types).
    /// </summary>
    private static string? AsString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out string? s) ? s : null;

    /// <summary>
    /// Calls the LBRY JSON-RPC proxy (<c>resolve</c>, <c>claim_search</c>, etc.) and returns the
    /// "result" payload, or null on any transport/protocol error (logged at Debug).
    /// </summary>
    private async Task<JsonNode?> CallApiProxyAsync(string method, JsonNode parameters, CancellationToken cancellationToken)
    {
        try
        {
            var payload = new JsonObject
            {
                ["method"] = method,
                ["params"] = parameters
            };

            using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json-rpc");
            using var response = await httpClient.PostAsync(ApiProxyUrl, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("LBRY API proxy call {Method} failed with {StatusCode}", method, response.StatusCode);
                }
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            var root = JsonNode.Parse(json);
            var error = root?["error"];
            if (error is not null)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("LBRY API proxy call {Method} returned an error: {Error}", method, error.ToJsonString());
                }
                return null;
            }

            return root?["result"];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(ex, "Error calling LBRY API proxy method {Method}", method);
            }
            return null;
        }
    }

    /// <summary>
    /// Resolves an Odysee channel handle (e.g. "@nathansifugaming:8") to its LBRY claim, giving
    /// access to the display name, description and avatar/cover images that yt-dlp's channel
    /// extractor discards.
    /// </summary>
    private async Task<ResolvedChannel?> ResolveChannelClaimAsync(string channelUrl, CancellationToken cancellationToken)
    {
        string handle = OdyseeChannelIdRegex().Match(channelUrl).Groups[1].Value;
        if (string.IsNullOrEmpty(handle))
        {
            return null;
        }

        // LBRY URIs use '#' as the claim-id separator; Odysee web URLs use ':'.
        string lbryUrl = $"lbry://{handle.Replace(':', '#')}";

        var parameters = new JsonObject { ["urls"] = lbryUrl };
        var result = await CallApiProxyAsync("resolve", parameters, cancellationToken);
        var claim = result?[lbryUrl];
        if (claim is null)
        {
            return null;
        }

        string? claimId = AsString(claim["claim_id"]);
        if (string.IsNullOrEmpty(claimId))
        {
            return null;
        }

        var value = claim["value"];
        string name = AsString(value?["title"]) ?? handle;
        string? avatarUrl = AsString(value?["thumbnail"]?["url"]);
        string? bannerUrl = AsString(value?["cover"]?["url"]);

        return new ResolvedChannel(claimId, name, AsString(value?["description"]), avatarUrl, bannerUrl);
    }

    private static List<ThumbnailInfo> BuildThumbnailList(string? avatarUrl, string? bannerUrl)
    {
        var list = new List<ThumbnailInfo>();
        if (!string.IsNullOrEmpty(avatarUrl))
        {
            list.Add(new ThumbnailInfo("avatar", avatarUrl, null, null, null));
        }

        if (!string.IsNullOrEmpty(bannerUrl) && !string.Equals(bannerUrl, avatarUrl, StringComparison.Ordinal))
        {
            list.Add(new ThumbnailInfo("banner", bannerUrl, null, null, null));
        }

        return list;
    }

    /// <summary>
    /// Odysee (LBRY) flat listings expose the thumbnail via the singular <c>thumbnail</c>
    /// field rather than the <c>thumbnails</c> array, so fall back to it when the array is empty.
    /// </summary>
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

    private static List<ThumbnailInfo> MapThumbnails(ThumbnailData[]? thumbnails) => thumbnails.IsNullOrEmpty()
        ? []
        : thumbnails!
            .Where(t => !string.IsNullOrEmpty(t.Url))
            .Select(t => new ThumbnailInfo(t.ID, t.Url!, t.Width, t.Height, t.Preference))
            .ToList();

    private List<VideoMetadata> MapEntriesToVideoMetadata(VideoData[]? entries, string channelId, string channelName)
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

            string videoId = x.ID ?? string.Empty;
            if (string.IsNullOrEmpty(videoId))
            {
                continue;
            }

            results.Add(new VideoMetadata
            {
                VideoId = videoId,
                Title = x.Title ?? "Unknown Title",
                Description = x.Description,
                // Odysee flat entries carry the canonical URL in "url" (webpage_url is null).
                Url = x.WebpageUrl ?? x.Url ?? string.Empty,
                ThumbnailUrl = PickThumbnail(x.Thumbnails, x.Thumbnail),
                Duration = x.Duration.HasValue ? TimeSpan.FromSeconds(x.Duration.Value) : null,
                // Odysee sets "timestamp" but not "upload_date" on flat entries.
                UploadDate = (x.UploadDate ?? x.Timestamp).AsUtc(),
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

    private sealed record ResolvedChannel(string ClaimId, string Name, string? Description, string? AvatarUrl, string? BannerUrl);
}
