using Hangfire;
using Humanizer;
using MyVideoArchive.Models.Api;
using MyVideoArchive.Models.Metadata;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for managing channel playlists
/// </summary>
[Authorize]
[ApiController]
[Route("api/channels/{channelId}/playlists")]
public class ChannelPlaylistsApiController : ControllerBase
{
    private readonly ILogger<ChannelPlaylistsApiController> logger;
    private readonly IConfiguration configuration;
    private readonly IUserContextService userContextService;
    private readonly IBackgroundJobClient backgroundJobClient;
    private readonly ThumbnailService thumbnailService;
    private readonly VideoMetadataProviderFactory metadataProviderFactory;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<Playlist> playlistRepository;
    private readonly IRepository<UserChannel> userChannelRepository;
    private readonly IRepository<UserPlaylist> userPlaylistRepository;

    public ChannelPlaylistsApiController(
        ILogger<ChannelPlaylistsApiController> logger,
        IConfiguration configuration,
        IUserContextService userContextService,
        IBackgroundJobClient backgroundJobClient,
        ThumbnailService thumbnailService,
        VideoMetadataProviderFactory metadataProviderFactory,
        IRepository<Channel> channelRepository,
        IRepository<Playlist> playlistRepository,
        IRepository<UserChannel> userChannelRepository,
        IRepository<UserPlaylist> userPlaylistRepository)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.userContextService = userContextService;
        this.backgroundJobClient = backgroundJobClient;
        this.thumbnailService = thumbnailService;
        this.metadataProviderFactory = metadataProviderFactory;
        this.channelRepository = channelRepository;
        this.playlistRepository = playlistRepository;
        this.userChannelRepository = userChannelRepository;
        this.userPlaylistRepository = userPlaylistRepository;
    }

    private async Task<bool> UserHasAccessToChannel(int channelId)
    {
        if (userContextService.IsAdministrator())
        {
            return true;
        }

        string? userId = userContextService.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return false;
        }

        var userChannel = await userChannelRepository.FindOneAsync(new SearchOptions<UserChannel>
        {
            Query = x =>
                x.UserId == userId &&
                x.ChannelId == channelId
        });

        return userChannel is not null;
    }

    /// <summary>
    /// Get all playlists for a channel (available, subscribed, and ignored), paginated
    /// </summary>
    [HttpGet("available")]
    public async Task<IActionResult> GetAvailablePlaylists(
        int channelId,
        [FromQuery] bool showIgnored = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 24)
    {
        try
        {
            // Check user access
            if (!await UserHasAccessToChannel(channelId))
            {
                return Forbid();
            }

            var channel = await channelRepository.FindOneAsync(channelId);
            if (channel is null)
            {
                return NotFound(new { message = "Channel not found" });
            }

            // Get metadata provider
            var provider = metadataProviderFactory.GetProviderByPlatform(channel.Platform);
            if (provider is null)
            {
                return BadRequest(new { message = $"No provider found for platform {channel.Platform}" });
            }

            var predicate = PredicateBuilder.New<Playlist>(x => x.ChannelId == channelId);

            if (!showIgnored)
            {
                predicate = predicate.And(x => !x.IsIgnored);
            }

            var options = new SearchOptions<Playlist>
            {
                CancellationToken = HttpContext.RequestAborted,
                Query = predicate,
                OrderBy = query => query
                    .OrderByDescending(p => p.SubscribedAt)
                    .ThenBy(p => p.Name),
                PageNumber = page,
                PageSize = pageSize
            };

            var playlists = await playlistRepository.FindAsync(options, x => new
            {
                x.Id,
                x.PlaylistId,
                x.Name,
                x.Description,
                x.Url,
                x.ThumbnailUrl,
                x.Platform,
                x.VideoCount,
                x.SubscribedAt,
                x.LastChecked,
                x.IsIgnored,
                IsSubscribed = x.SubscribedAt != default
            });

            return Ok(new
            {
                playlists,
                pagination = new
                {
                    currentPage = page,
                    pageSize,
                    totalCount = playlists.ItemCount,
                    totalPages = playlists.PageCount
                }
            });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving playlists for channel {ChannelId}", channelId);
            }

            return StatusCode(500, new { message = "An error occurred while retrieving playlists" });
        }
    }

    /// <summary>
    /// Subscribe to selected playlists
    /// </summary>
    [HttpPost("subscribe")]
    public async Task<IActionResult> SubscribePlaylists(int channelId, [FromBody] SubscribePlaylistsRequest request)
    {
        try
        {
            // Check user access
            if (!await UserHasAccessToChannel(channelId))
            {
                return Forbid();
            }

            if (request.PlaylistIds.IsNullOrEmpty())
            {
                return BadRequest(new { message = "No playlist IDs provided" });
            }

            var channel = await channelRepository.FindOneAsync(channelId);
            if (channel is null)
            {
                return NotFound(new { message = "Channel not found" });
            }

            string? userId = userContextService.GetCurrentUserId();

            int subscribedCount = 0;
            foreach (int playlistId in request.PlaylistIds)
            {
                var playlist = await playlistRepository.FindOneAsync(playlistId);
                if (playlist is not null && playlist.ChannelId == channelId)
                {
                    // Update SubscribedAt if not already subscribed
                    if (playlist.SubscribedAt == DateTime.MinValue)
                    {
                        playlist.SubscribedAt = DateTime.UtcNow;
                        await playlistRepository.UpdateAsync(playlist);
                    }

                    // Check if user already subscribed
                    var existingSubscription = await userPlaylistRepository.FindOneAsync(new SearchOptions<UserPlaylist>
                    {
                        Query = x =>
                            x.UserId == userId &&
                            x.PlaylistId == playlistId
                    });

                    if (existingSubscription is null)
                    {
                        // Create user subscription
                        await userPlaylistRepository.InsertAsync(new UserPlaylist
                        {
                            UserId = userId!,
                            PlaylistId = playlistId,
                            SubscribedAt = DateTime.UtcNow
                        });
                    }

                    // Queue sync job for the playlist
                    backgroundJobClient.Enqueue<PlaylistSyncJob>(job =>
                        job.ExecuteAsync(playlist.Id, CancellationToken.None));
                    subscribedCount++;
                }
            }

            return Ok(new
            {
                message = $"Queued sync for {subscribedCount} playlist(s)",
                subscribedCount
            });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error subscribing to playlists for channel {ChannelId}", channelId);
            }

            return StatusCode(500, new { message = "An error occurred while subscribing to playlists" });
        }
    }

    /// <summary>
    /// Subscribe to all playlists for a channel
    /// </summary>
    [HttpPost("subscribe-all")]
    public async Task<IActionResult> SubscribeAllPlaylists(int channelId)
    {
        try
        {
            // Check user access
            if (!await UserHasAccessToChannel(channelId))
            {
                return Forbid();
            }

            string? userId = userContextService.GetCurrentUserId();

            var playlists = await playlistRepository.FindAsync(new SearchOptions<Playlist>
            {
                Query = x =>
                    x.ChannelId == channelId &&
                    !x.IsIgnored
            });

            if (playlists.Count == 0)
            {
                return Ok(new { message = "No playlists available to subscribe", subscribedCount = 0 });
            }

            var playlistIds = playlists.Select(p => p.Id).ToList();
            var userPlaylistSubscriptions = await userPlaylistRepository.FindAsync(new SearchOptions<UserPlaylist>
            {
                Query = x =>
                    x.UserId == userId &&
                    playlistIds.Contains(x.PlaylistId)
            });

            var playlistUpdates = new List<Playlist>();
            var userPlaylistInserts = new List<UserPlaylist>();
            foreach (var playlist in playlists)
            {
                // Update SubscribedAt if not already subscribed
                if (playlist.SubscribedAt == DateTime.MinValue)
                {
                    playlist.SubscribedAt = DateTime.UtcNow;
                    playlistUpdates.Add(playlist);
                }

                // Check if user already subscribed
                var existingSubscription = userPlaylistSubscriptions.FirstOrDefault(x => x.PlaylistId == playlist.Id);

                if (existingSubscription is null)
                {
                    // Create user subscription
                    userPlaylistInserts.Add(new UserPlaylist
                    {
                        UserId = userId!,
                        PlaylistId = playlist.Id,
                        SubscribedAt = DateTime.UtcNow
                    });
                }

                backgroundJobClient.Enqueue<PlaylistSyncJob>(job =>
                    job.ExecuteAsync(playlist.Id, CancellationToken.None));
            }

            await playlistRepository.UpdateAsync(playlistUpdates);
            await userPlaylistRepository.InsertAsync(userPlaylistInserts);

            return Ok(new
            {
                message = $"Queued sync for {playlists.Count} playlist(s)",
                subscribedCount = playlists.Count
            });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error subscribing to all playlists for channel {ChannelId}", channelId);
            }

            return StatusCode(500, new { message = "An error occurred while subscribing to playlists" });
        }
    }

    /// <summary>
    /// Toggle ignore status for a playlist
    /// </summary>
    [HttpPut("{playlistId}/ignore")]
    public async Task<IActionResult> ToggleIgnore(int channelId, int playlistId, [FromBody] IgnorePlaylistRequest request)
    {
        try
        {
            // Check user access
            if (!await UserHasAccessToChannel(channelId))
            {
                return Forbid();
            }

            var playlist = await playlistRepository.FindOneAsync(new SearchOptions<Playlist>
            {
                Query = x =>
                    x.Id == playlistId &&
                    x.ChannelId == channelId
            });

            if (playlist is null)
            {
                return NotFound(new { message = "Playlist not found" });
            }

            playlist.IsIgnored = request.IsIgnored;
            await playlistRepository.UpdateAsync(playlist);

            return Ok(new
            {
                message = request.IsIgnored ? "Playlist ignored" : "Playlist unignored",
                isIgnored = playlist.IsIgnored
            });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error toggling ignore status for playlist {PlaylistId}", playlistId);
            }

            return StatusCode(500, new { message = "An error occurred while updating playlist status" });
        }
    }

    /// <summary>
    /// Refresh playlists from YouTube for a channel
    /// </summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshPlaylists(int channelId)
    {
        try
        {
            // Check user access
            if (!await UserHasAccessToChannel(channelId))
            {
                return Forbid();
            }

            var channel = await channelRepository.FindOneAsync(channelId);
            if (channel is null)
            {
                return NotFound(new { message = "Channel not found" });
            }

            // Get metadata provider
            var provider = metadataProviderFactory.GetProviderByPlatform(channel.Platform);
            if (provider is null)
            {
                return BadRequest(new { message = $"No provider found for platform {channel.Platform}" });
            }

            // Fetch playlists from YouTube
            var playlistMetadataList = await provider.GetChannelPlaylistsAsync(channel.Url);

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Found {Count} playlists for channel {ChannelId}", playlistMetadataList.Count, channelId);
            }

            int newPlaylistsCount = 0;
            var existingPlaylists = await playlistRepository.FindAsync(new SearchOptions<Playlist>
            {
                Query = x => x.ChannelId == channelId
            });

            var existingPlaylistIds = existingPlaylists.Select(p => p.PlaylistId).ToHashSet();

            string downloadPath = configuration.GetValue<string>("VideoDownload:OutputPath")
                ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

            var playlistUpdates = new List<Playlist>();
            var playlistInserts = new List<Playlist>();
            foreach (var playlistMetadata in playlistMetadataList)
            {
                if (existingPlaylistIds.Contains(playlistMetadata.PlaylistId))
                {
                    // Update existing playlist
                    var existingPlaylist = existingPlaylists.First(x => x.PlaylistId == playlistMetadata.PlaylistId);
                    existingPlaylist.Name = playlistMetadata.Name;
                    existingPlaylist.Description = playlistMetadata.Description;
                    existingPlaylist.Url = playlistMetadata.Url;

                    // Only overwrite ThumbnailUrl if it has not already been downloaded and
                    // stored as a base64 data URL – external URLs expire and cause 404s.
                    if (!existingPlaylist.ThumbnailUrl?.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ?? true)
                    {
                        string channelDirId = channel.ChannelId;
                        string playlistThumbnailDir = Path.Combine(downloadPath, channelDirId, "Playlists");

                        existingPlaylist.ThumbnailUrl = await thumbnailService.DownloadAndSaveAsync(
                            playlistMetadata.ThumbnailUrl, playlistThumbnailDir, existingPlaylist.PlaylistId)
                            ?? playlistMetadata.ThumbnailUrl;
                    }

                    playlistUpdates.Add(existingPlaylist);
                }
                else
                {
                    // Create new playlist entry
                    playlistInserts.Add(new Playlist
                    {
                        PlaylistId = playlistMetadata.PlaylistId,
                        Name = playlistMetadata.Name,
                        Description = playlistMetadata.Description,
                        Url = playlistMetadata.Url,
                        ThumbnailUrl = playlistMetadata.ThumbnailUrl,
                        Platform = playlistMetadata.Platform,
                        SubscribedAt = DateTime.MinValue, // Not subscribed yet
                        IsIgnored = false,
                        ChannelId = channelId
                    });
                    newPlaylistsCount++;
                }
            }

            await playlistRepository.InsertAsync(playlistInserts);
            await playlistRepository.UpdateAsync(playlistUpdates);

            return Ok(new
            {
                message = $"Refreshed playlists. Found {playlistMetadataList.Count} total, {newPlaylistsCount} new",
                totalCount = playlistMetadataList.Count,
                newCount = newPlaylistsCount
            });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error refreshing playlists for channel {ChannelId}", channelId);
            }

            return StatusCode(500, new { message = "An error occurred while refreshing playlists" });
        }
    }
}