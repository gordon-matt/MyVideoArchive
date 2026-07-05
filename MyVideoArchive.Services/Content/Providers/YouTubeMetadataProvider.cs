using System.Text.RegularExpressions;
using MyVideoArchive.Models.Metadata;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
using YoutubeDLSharp.Options;

namespace MyVideoArchive.Services.Content.Providers;

/// <summary>
/// YouTube implementation of metadata provider using yt-dlp
/// </summary>
public partial class YouTubeMetadataProvider : IVideoMetadataProvider
{
    private readonly ILogger<YouTubeMetadataProvider> logger;
    private readonly IConfiguration configuration;
    private readonly YoutubeDL ytdl;

    // Serializes all metadata fetches (channel/playlist/video "info" lookups) across every
    // caller — ChannelSyncJob, PlaylistSyncJob, MetadataReviewJob, etc. all resolve the same
    // singleton instance of this provider. Without this, e.g. subscribing to many playlists at
    // once (PlaylistService.SubscribeToPlaylistsAsync) fires one Hangfire job per playlist onto
    // the "default" queue, which has no worker-count cap, so dozens of yt-dlp "browse" requests
    // can hit YouTube in parallel from the same IP. That burst is what YouTube's bot detection
    // reacts to with "HTTP Error 404" (webpage) followed by "HTTP Error 400: Request contains an
    // invalid argument" (API) — the same protection already exists for downloads (see the
    // dedicated single-worker "downloads" Hangfire queue) but wasn't applied to metadata lookups.
    private static readonly SemaphoreSlim requestGate = new(1, 1);

    public YouTubeMetadataProvider(
        ILogger<YouTubeMetadataProvider> logger,
        IConfiguration configuration,
        YoutubeDL ytdl)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.ytdl = ytdl;
    }

    /// <summary>
    /// Runs a yt-dlp metadata fetch, serialized against every other metadata fetch and paced
    /// with a short randomized delay afterwards so consecutive requests (e.g. across many
    /// playlists in a channel) don't look like a scripted burst to YouTube.
    /// </summary>
    private async Task<RunResult<VideoData>> FetchDataAsync(
        string url, OptionSet? overrideOptions, CancellationToken cancellationToken)
    {
        var options = overrideOptions ?? new OptionSet();
        options.ApplyConfiguredExtractorArgs(configuration);

        await requestGate.WaitAsync(cancellationToken);
        try
        {
            return await ytdl.RunVideoDataFetch(url, ct: cancellationToken, overrideOptions: options);
        }
        finally
        {
            int minDelay = configuration.GetValue<int>("YoutubeDL:MetadataThrottle:MinDelaySeconds", 3);
            int maxDelay = configuration.GetValue<int>("YoutubeDL:MetadataThrottle:MaxDelaySeconds", 8);
            if (maxDelay > minDelay)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(minDelay, maxDelay + 1)), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Only paces the *next* request; don't fail the caller that already succeeded.
                }
            }

            requestGate.Release();
        }
    }

    public string PlatformName => "YouTube";

    public string BuildChannelUrl(string channelId) => $"https://www.youtube.com/channel/{channelId}";

    public bool CanHandle(string url) => YouTubeUrlRegex().IsMatch(url);

    public async Task<ChannelMetadata?> GetChannelMetadataAsync(string channelUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Fetching channel metadata for: {Url}", channelUrl);
            }

            var result = await FetchDataAsync(channelUrl, overrideOptions: null, cancellationToken);

            if (!result.Success || result.Data is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed to fetch channel metadata for {Url}: {Error}", channelUrl, string.Join(", ", result.ErrorOutput));
                }

                return null;
            }

            var data = result.Data;

            var thumbnailInfos = MapThumbnails(data.Thumbnails);
            return new ChannelMetadata
            {
                ChannelId = data.ChannelID,
                Name = data.Channel ?? data.Uploader ?? "Unknown Channel",
                Url = data.WebpageUrl ?? channelUrl,
                Description = data.Description,
                BannerUrl = GetBestBannerThumbnail(data.Thumbnails),
                AvatarUrl = GetAvatarOrBestThumbnail(data.Thumbnails),
                Thumbnails = thumbnailInfos,
                SubscriberCount = data.ChannelFollowerCount is not null ? (int?)data.ChannelFollowerCount : null,
                Platform = PlatformName,
                Tags = data.Tags?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? []
            };
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error fetching channel metadata for {Url}", channelUrl);
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
                logger.LogInformation("Fetching playlists for channel: {Url}", channelUrl);
            }

            // Use yt-dlp to get channel playlists
            // The trick is to use the channel's playlists URL
            string playlistsUrl = channelUrl.TrimEnd('/') + "/playlists";
            string releasesUrl = channelUrl.TrimEnd('/') + "/releases";
            string coursesUrl = channelUrl.TrimEnd('/') + "/courses";

            var regularPlaylists = await GetChannelPlaylistsInternalAsync(channelUrl, playlistsUrl, cancellationToken);
            var releasePlaylists = await GetChannelPlaylistsInternalAsync(channelUrl, releasesUrl, cancellationToken);
            var coursePlaylists = await GetChannelPlaylistsInternalAsync(channelUrl, coursesUrl, cancellationToken);

            var playlists = regularPlaylists
                .Concat(releasePlaylists)
                .Concat(coursePlaylists)
                .ToList();

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Found {Count} playlists for channel {Url}", playlists.Count, channelUrl);
            }

            return playlists;
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error fetching playlists for channel {Url}", channelUrl);
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
                logger.LogInformation("Fetching channel videos for: {Url}", channelUrl);
            }

            // Append /videos to ensure we get all videos
            string videosUrl = channelUrl.TrimEnd('/') + "/videos";

            var options = new OptionSet
            {
                FlatPlaylist = true,
                DumpSingleJson = true
            };

            var result = await FetchDataAsync(videosUrl, options, cancellationToken);

            if (!result.Success || result.Data is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed to fetch channel videos for {Url}: {Error}", channelUrl, string.Join(", ", result.ErrorOutput));
                }

                return [];
            }

            return result.Data.Entries
                .Select(x => new VideoMetadata
                {
                    VideoId = x.ID ?? string.Empty,
                    Title = x.Title ?? "Unknown Title",
                    Description = x.Description,
                    Url = x.Url ?? string.Empty,
                    ThumbnailUrl = GetBestThumbnail(x.Thumbnails),
                    Duration = x.Duration.HasValue ? TimeSpan.FromSeconds(x.Duration.Value) : null,
                    UploadDate = x.UploadDate.AsUtc(),
                    ViewCount = x.ViewCount.HasValue ? (int?)x.ViewCount : null,
                    LikeCount = x.LikeCount.HasValue ? (int?)x.LikeCount : null,
                    ChannelId = x.ChannelID,
                    ChannelName = x.Channel ?? x.Uploader ?? "Unknown Channel",
                    Platform = PlatformName,
                    PlaylistId = null
                })
                .ToList();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error fetching channel videos for {Url}", channelUrl);
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
                logger.LogInformation("Fetching playlist metadata for: {Url}", playlistUrl);
            }

            var options = new OptionSet
            {
                FlatPlaylist = true,
                DumpSingleJson = true
            };

            var result = await FetchDataAsync(playlistUrl, options, cancellationToken);

            if (!result.Success || result.Data is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed to fetch playlist metadata for {Url}: {Error}", playlistUrl, string.Join(", ", result.ErrorOutput));
                }

                return null;
            }

            var data = result.Data;

            return new PlaylistMetadata
            {
                PlaylistId = data.ID ?? string.Empty,
                Name = data.Title ?? "Unknown Playlist",
                Url = data.WebpageUrl ?? playlistUrl,
                ThumbnailUrl = GetBestThumbnail(data.Thumbnails),
                Description = data.Description,
                ChannelId = data.ChannelID,
                ChannelName = data.Channel ?? data.Uploader ?? "Unknown Channel",
                Platform = PlatformName,
                Tags = data.Tags?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? []
            };
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error fetching playlist metadata for {Url}", playlistUrl);
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
                logger.LogInformation("Fetching playlist videos for: {Url}", playlistUrl);
            }

            var options = new OptionSet
            {
                FlatPlaylist = true,
                DumpSingleJson = true
            };

            var result = await FetchDataAsync(playlistUrl, options, cancellationToken);

            if (!result.Success || result.Data is null)
            {
                logger.LogWarning("Failed to fetch playlist videos for {Url}: {Error}", playlistUrl, string.Join(", ", result.ErrorOutput));
                return [];
            }

            return result.Data.Entries
                .Select(x => new VideoMetadata
                {
                    VideoId = x.ID ?? string.Empty,
                    Title = x.Title ?? "Unknown Title",
                    Description = x.Description,
                    Url = x.Url ?? string.Empty,
                    ThumbnailUrl = GetBestThumbnail(x.Thumbnails),
                    Duration = x.Duration.HasValue ? TimeSpan.FromSeconds(x.Duration.Value) : null,
                    UploadDate = x.UploadDate.AsUtc(),
                    ViewCount = x.ViewCount.HasValue ? (int?)x.ViewCount : null,
                    LikeCount = x.LikeCount.HasValue ? (int?)x.LikeCount : null,
                    ChannelId = x.ChannelID,
                    ChannelName = x.Channel ?? x.Uploader ?? "Unknown Channel",
                    Platform = PlatformName,
                    PlaylistId = null
                })
                .ToList();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error fetching playlist videos for {Url}", playlistUrl);
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
                logger.LogInformation("Fetching video metadata for: {Url}", videoUrl);
            }

            var result = await FetchDataAsync(videoUrl, overrideOptions: null, cancellationToken);

            if (!result.Success || result.Data is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed to fetch video metadata for {Url}: {Error}", videoUrl, string.Join(", ", result.ErrorOutput));
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
                ChannelId = data.ChannelID,
                ChannelName = data.Channel ?? data.Uploader ?? "Unknown Channel",
                Platform = PlatformName,
                PlaylistId = null,
                Tags = data.Tags?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? []
            };
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error fetching video metadata for {Url}", videoUrl);
            }
            return null;
        }
    }

    /// <summary>
    /// Returns the avatar thumbnail URL for a channel by looking for a thumbnail whose ID
    /// contains "avatar". Returns null if no avatar thumbnail is found.
    /// </summary>
    private static string? GetAvatarOrBestThumbnail(ThumbnailData[]? thumbnails)
    {
        if (thumbnails.IsNullOrEmpty())
        {
            return null;
        }

        var result = thumbnails!
            .Where(t =>
                !string.IsNullOrEmpty(t.Url))
            .FirstOrDefault(x => x.Resolution == "900x900");

        if (result is not null)
        {
            return result.Url;
        }

        return GetBestThumbnail(thumbnails);
    }

    /// <summary>
    /// Returns the best banner thumbnail URL for a channel, preferring thumbnails whose ID
    /// contains "banner". Falls back to the highest-resolution thumbnail if none found.
    /// </summary>
    private static string? GetBestBannerThumbnail(ThumbnailData[]? thumbnails)
    {
        return GetBestThumbnail(thumbnails);
    }

    private static string? GetBestThumbnail(ThumbnailData[]? thumbnails)
    {
        if (thumbnails.IsNullOrEmpty())
        {
            return null;
        }

        // Prefer higher resolution thumbnails
        var best = thumbnails!
            .Where(t => !string.IsNullOrEmpty(t.Url))
            .OrderByDescending(t => (t.Width ?? 0) * (t.Height ?? 0))
            .FirstOrDefault();

        return best?.Url;
    }

    private static List<ThumbnailInfo> MapThumbnails(ThumbnailData[]? thumbnails) => thumbnails.IsNullOrEmpty()
        ? []
        : thumbnails!
            .Where(t => !string.IsNullOrEmpty(t.Url))
            .Select(t => new ThumbnailInfo(t.ID, t.Url!, t.Width, t.Height, t.Preference))
            .ToList();

    [GeneratedRegex(@"(youtube\.com|youtu\.be)", RegexOptions.IgnoreCase)]
    private static partial Regex YouTubeUrlRegex();

    private async Task<List<PlaylistMetadata>> GetChannelPlaylistsInternalAsync(string channelUrl, string playlistsUrl, CancellationToken cancellationToken = default)
    {
        var options = new OptionSet
        {
            FlatPlaylist = true,
            DumpSingleJson = true
        };

        var result = await FetchDataAsync(playlistsUrl, options, cancellationToken);

        if (!result.Success || result.Data is null)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("Failed to fetch playlists for {Url}: {Error}", channelUrl, string.Join(", ", result.ErrorOutput));
            }

            return [];
        }

        var playlists = new List<PlaylistMetadata>();

        // The result should contain entries that are playlists
        if (!result.Data.Entries.IsNullOrEmpty())
        {
            foreach (var entry in result.Data.Entries)
            {
                // Each entry represents a playlist
                if (!string.IsNullOrEmpty(entry.ID))
                {
                    playlists.Add(new PlaylistMetadata
                    {
                        PlaylistId = entry.ID,
                        Name = entry.Title ?? "Unknown Playlist",
                        Url = entry.Url ?? entry.WebpageUrl ?? $"https://www.youtube.com/playlist?list={entry.ID}",
                        Description = entry.Description,
                        ThumbnailUrl = GetBestThumbnail(entry.Thumbnails),
                        ChannelId = result.Data.ChannelID ?? result.Data.Channel ?? result.Data.Uploader ?? string.Empty,
                        ChannelName = result.Data.Channel ?? result.Data.Uploader ?? "Unknown Channel",
                        //VideoCount = entry.PlaylistCount, //PlaylistCount does not exist
                        Platform = PlatformName
                    });
                }
            }
        }
        return playlists;
    }
}