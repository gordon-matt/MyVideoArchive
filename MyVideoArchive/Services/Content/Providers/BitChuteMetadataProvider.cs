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

    public string PlatformName => "BitChute";

    [GeneratedRegex(@"bitchute\.com", RegexOptions.IgnoreCase)]
    private static partial Regex BitChuteUrlRegex();

    [GeneratedRegex(@"bitchute\.com/channel/([^/?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BitChuteChannelIdRegex();

    [GeneratedRegex(@"bitchute\.com/playlist/([^/?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BitChutePlaylistIdRegex();

    public BitChuteMetadataProvider(ILogger<BitChuteMetadataProvider> logger, YoutubeDL ytdl)
    {
        this.logger = logger;
        this.ytdl = ytdl;
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

            return new ChannelMetadata
            {
                ChannelId = channelId,
                Name = data.Channel ?? data.Uploader ?? data.Title ?? "Unknown Channel",
                Url = data.WebpageUrl ?? channelUrl,
                Description = data.Description,
                ThumbnailUrl = GetBestThumbnail(data.Thumbnails),
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

            return new VideoMetadata
            {
                VideoId = data.ID ?? string.Empty,
                Title = data.Title ?? "Unknown Title",
                Description = data.Description,
                Url = data.WebpageUrl ?? videoUrl,
                ThumbnailUrl = GetBestThumbnail(data.Thumbnails),
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
                ThumbnailUrl = GetBestThumbnail(data.Thumbnails),
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

            return MapEntriesToVideoMetadata(result.Data.Entries, channelId, channelName);
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

            return MapEntriesToVideoMetadata(result.Data.Entries, channelId, channelName);
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
    public Task<List<PlaylistMetadata>> GetChannelPlaylistsAsync(string channelUrl, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<List<PlaylistMetadata>>([]);
    }

    private List<VideoMetadata> MapEntriesToVideoMetadata(VideoData[]? entries, string fallbackChannelId, string fallbackChannelName)
    {
        if (entries is null or { Length: 0 })
        {
            return [];
        }

        return entries
            .Select(x => new VideoMetadata
            {
                VideoId = x.ID ?? string.Empty,
                Title = x.Title ?? "Unknown Title",
                Description = x.Description,
                Url = x.Url ?? x.WebpageUrl ?? $"https://www.bitchute.com/video/{x.ID}/",
                ThumbnailUrl = GetBestThumbnail(x.Thumbnails),
                Duration = x.Duration.HasValue ? TimeSpan.FromSeconds(x.Duration.Value) : null,
                UploadDate = x.UploadDate.AsUtc(),
                ViewCount = x.ViewCount.HasValue ? (int?)x.ViewCount : null,
                LikeCount = x.LikeCount.HasValue ? (int?)x.LikeCount : null,
                ChannelId = x.ChannelID ?? x.UploaderID ?? fallbackChannelId,
                ChannelName = x.Channel ?? x.Uploader ?? fallbackChannelName,
                Platform = PlatformName,
                PlaylistId = null
            })
            .ToList();
    }

    private static string? GetBestThumbnail(ThumbnailData[]? thumbnails)
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
}
