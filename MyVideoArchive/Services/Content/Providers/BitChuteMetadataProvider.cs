using System.Text.RegularExpressions;
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
    private readonly ILogger<BitChuteMetadataProvider> logger;
    private readonly YoutubeDL ytdl;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly HttpClient thumbnailHttpClient;

    public string PlatformName => "BitChute";

    [GeneratedRegex(@"bitchute\.com", RegexOptions.IgnoreCase)]
    private static partial Regex BitChuteUrlRegex();

    [GeneratedRegex(@"bitchute\.com/channel/([^/?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BitChuteChannelIdRegex();

    [GeneratedRegex(@"bitchute\.com/playlist/([^/?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BitChutePlaylistIdRegex();

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

    public bool CanHandle(string url) => BitChuteUrlRegex().IsMatch(url);

    public string BuildChannelUrl(string channelId) => $"https://www.bitchute.com/channel/{channelId}/";

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

    /// <summary>
    /// BitChute does not expose a playlists listing on a channel page. Returns an empty list.
    /// Users must subscribe to individual playlist URLs (bitchute.com/playlist/{id}).
    /// </summary>
    public Task<List<PlaylistMetadata>> GetChannelPlaylistsAsync(string channelUrl, CancellationToken cancellationToken = default) => Task.FromResult<List<PlaylistMetadata>>([]);

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

    private static List<ThumbnailInfo> MapThumbnails(ThumbnailData[]? thumbnails)
    {
        if (thumbnails.IsNullOrEmpty()) return [];
        return thumbnails!
            .Where(t => !string.IsNullOrEmpty(t.Url))
            .Select(t => new ThumbnailInfo(t.ID, t.Url!, (int?)t.Width, (int?)t.Height, (int?)t.Preference))
            .ToList();
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