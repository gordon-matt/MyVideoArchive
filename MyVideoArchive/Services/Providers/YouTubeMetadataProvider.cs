using MyVideoArchive.Models.Metadata;
using MyVideoArchive.Services.Abstractions;
using System.Text.RegularExpressions;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
using YoutubeDLSharp.Options;

namespace MyVideoArchive.Services.Providers;

/// <summary>
/// YouTube implementation of metadata provider using yt-dlp
/// </summary>
public partial class YouTubeMetadataProvider : IVideoMetadataProvider
{
    private readonly ILogger<YouTubeMetadataProvider> _logger;
    private readonly YoutubeDL _ytdl;

    public string PlatformName => "YouTube";

    [GeneratedRegex(@"(youtube\.com|youtu\.be)", RegexOptions.IgnoreCase)]
    private static partial Regex YouTubeUrlRegex();

    public YouTubeMetadataProvider(ILogger<YouTubeMetadataProvider> logger, YoutubeDL youtubeDL)
    {
        _logger = logger;
        _ytdl = youtubeDL;
    }

    public bool CanHandle(string url)
    {
        return YouTubeUrlRegex().IsMatch(url);
    }

    public async Task<ChannelMetadata?> GetChannelMetadataAsync(string channelUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching channel metadata for: {Url}", channelUrl);

            var result = await _ytdl.RunVideoDataFetch(channelUrl);

            if (!result.Success || result.Data == null)
            {
                _logger.LogWarning("Failed to fetch channel metadata for {Url}: {Error}", channelUrl, string.Join(", ", result.ErrorOutput));
                return null;
            }

            var data = result.Data;

            return new ChannelMetadata
            {
                ChannelId = data.Channel ?? data.Uploader ?? data.ID ?? string.Empty,
                Name = data.Channel ?? data.Uploader ?? "Unknown Channel",
                Url = data.WebpageUrl ?? channelUrl,
                Description = data.Description,
                ThumbnailUrl = GetBestThumbnail(data.Thumbnails),
                SubscriberCount = data.ChannelFollowerCount != null ? (int?)data.ChannelFollowerCount : null,
                Platform = PlatformName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching channel metadata for {Url}", channelUrl);
            return null;
        }
    }

    public async Task<VideoMetadata?> GetVideoMetadataAsync(string videoUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching video metadata for: {Url}", videoUrl);

            var result = await _ytdl.RunVideoDataFetch(videoUrl);

            if (!result.Success || result.Data == null)
            {
                _logger.LogWarning("Failed to fetch video metadata for {Url}: {Error}", videoUrl, string.Join(", ", result.ErrorOutput));
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
                UploadDate = data.UploadDate,
                ViewCount = data.ViewCount.HasValue ? (int?)data.ViewCount : null,
                LikeCount = data.LikeCount.HasValue ? (int?)data.LikeCount : null,
                ChannelId = data.Channel ?? data.Uploader ?? data.ID ?? string.Empty,
                ChannelName = data.Channel ?? data.Uploader ?? "Unknown Channel",
                Platform = PlatformName,
                PlaylistId = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching video metadata for {Url}", videoUrl);
            return null;
        }
    }

    public async Task<PlaylistMetadata?> GetPlaylistMetadataAsync(string playlistUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching playlist metadata for: {Url}", playlistUrl);

            var options = new OptionSet
            {
                FlatPlaylist = true,
                DumpSingleJson = true
            };

            var result = await _ytdl.RunVideoDataFetch(playlistUrl, overrideOptions: options);

            if (!result.Success || result.Data == null)
            {
                _logger.LogWarning("Failed to fetch playlist metadata for {Url}: {Error}", playlistUrl, string.Join(", ", result.ErrorOutput));
                return null;
            }

            var data = result.Data;

            return new PlaylistMetadata
            {
                PlaylistId = data.ID ?? string.Empty,
                Name = data.Title ?? "Unknown Playlist",
                Url = data.WebpageUrl ?? playlistUrl,
                Description = data.Description,
                ChannelId = data.Channel ?? data.Uploader ?? string.Empty,
                ChannelName = data.Channel ?? data.Uploader ?? "Unknown Channel",
                VideoCount = null,
                Platform = PlatformName,
                VideoIds = []
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching playlist metadata for {Url}", playlistUrl);
            return null;
        }
    }

    public async Task<List<VideoMetadata>> GetChannelVideosAsync(string channelUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching channel videos for: {Url}", channelUrl);

            // Append /videos to ensure we get all videos
            var videosUrl = channelUrl.TrimEnd('/') + "/videos";

            var options = new OptionSet
            {
                FlatPlaylist = true,
                DumpSingleJson = true
            };

            var result = await _ytdl.RunVideoDataFetch(videosUrl, overrideOptions: options);

            if (!result.Success || result.Data == null)
            {
                _logger.LogWarning("Failed to fetch channel videos for {Url}: {Error}", channelUrl, string.Join(", ", result.ErrorOutput));
                return [];
            }

            return result.Data.Entries
                .Select(x => new VideoMetadata
                {
                    VideoId = x.ID ?? string.Empty,
                    Title = x.Title ?? "Unknown Title",
                    Description = x.Description,
                    Url = x.WebpageUrl ?? string.Empty,
                    ThumbnailUrl = GetBestThumbnail(x.Thumbnails),
                    Duration = x.Duration.HasValue ? TimeSpan.FromSeconds(x.Duration.Value) : null,
                    UploadDate = x.UploadDate,
                    ViewCount = x.ViewCount.HasValue ? (int?)x.ViewCount : null,
                    LikeCount = x.LikeCount.HasValue ? (int?)x.LikeCount : null,
                    ChannelId = x.Channel ?? x.Uploader ?? string.Empty,
                    ChannelName = x.Channel ?? x.Uploader ?? "Unknown Channel",
                    Platform = PlatformName,
                    PlaylistId = null
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching channel videos for {Url}", channelUrl);
            return [];
        }
    }

    public async Task<List<VideoMetadata>> GetPlaylistVideosAsync(string playlistUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching playlist videos for: {Url}", playlistUrl);

            var options = new OptionSet
            {
                FlatPlaylist = true,
                DumpSingleJson = true
            };

            var result = await _ytdl.RunVideoDataFetch(playlistUrl, overrideOptions: options);

            if (!result.Success || result.Data == null)
            {
                _logger.LogWarning("Failed to fetch playlist videos for {Url}: {Error}", playlistUrl, string.Join(", ", result.ErrorOutput));
                return [];
            }

            var videos = new List<VideoMetadata>();

            // For playlist videos, flat data returns basic info
            // Return minimal result for now
            _logger.LogInformation("Playlist data fetched for {Url}, detailed video fetching not implemented yet", playlistUrl);

            return videos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching playlist videos for {Url}", playlistUrl);
            return [];
        }
    }

    private static string? GetBestThumbnail(ThumbnailData[]? thumbnails)
    {
        if (thumbnails == null || thumbnails.Length == 0)
            return null;

        // Prefer higher resolution thumbnails
        var best = thumbnails
            .Where(t => !string.IsNullOrEmpty(t.Url))
            .OrderByDescending(t => (t.Width ?? 0) * (t.Height ?? 0))
            .FirstOrDefault();

        return best?.Url;
    }

    private static DateTime? ParseUploadDate(DateTime? uploadDate)
    {
        return uploadDate;
    }
}
