using Extenso.Data.Entity;
using Hangfire;
using LinqKit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyVideoArchive.Data.Entities;
using MyVideoArchive.Services;
using MyVideoArchive.Services.Jobs;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for managing channel playlists
/// </summary>
[Authorize]
[ApiController]
[Route("api/channels/{channelId}/playlists")]
public class ChannelPlaylistsApiController : ControllerBase
{
    private readonly IRepository<Playlist> _playlistRepository;
    private readonly IRepository<Channel> _channelRepository;
    private readonly IRepository<UserChannel> _userChannelRepository;
    private readonly IRepository<UserPlaylist> _userPlaylistRepository;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly VideoMetadataProviderFactory _metadataProviderFactory;
    private readonly IUserContextService _userContext;
    private readonly ILogger<ChannelPlaylistsApiController> _logger;

    public ChannelPlaylistsApiController(
        IRepository<Playlist> playlistRepository,
        IRepository<Channel> channelRepository,
        IRepository<UserChannel> userChannelRepository,
        IRepository<UserPlaylist> userPlaylistRepository,
        IBackgroundJobClient backgroundJobClient,
        VideoMetadataProviderFactory metadataProviderFactory,
        IUserContextService userContext,
        ILogger<ChannelPlaylistsApiController> logger)
    {
        _playlistRepository = playlistRepository;
        _channelRepository = channelRepository;
        _userChannelRepository = userChannelRepository;
        _userPlaylistRepository = userPlaylistRepository;
        _backgroundJobClient = backgroundJobClient;
        _metadataProviderFactory = metadataProviderFactory;
        _userContext = userContext;
        _logger = logger;
    }

    private async Task<bool> UserHasAccessToChannel(int channelId)
    {
        if (_userContext.IsAdministrator())
        {
            return true;
        }

        string? userId = _userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return false;
        }

        var userChannel = await _userChannelRepository.FindOneAsync(new SearchOptions<UserChannel>
        {
            Query = uc => uc.UserId == userId && uc.ChannelId == channelId
        });

        return userChannel != null;
    }

    /// <summary>
    /// Get all playlists for a channel (available, subscribed, and ignored)
    /// </summary>
    [HttpGet("available")]
    public async Task<IActionResult> GetAvailablePlaylists(
        int channelId,
        [FromQuery] bool showIgnored = false)
    {
        try
        {
            // Check user access
            if (!await UserHasAccessToChannel(channelId))
            {
                return Forbid();
            }

            var channel = await _channelRepository.FindOneAsync(channelId);
            if (channel == null)
            {
                return NotFound(new { message = "Channel not found" });
            }

            // Get metadata provider
            var provider = _metadataProviderFactory.GetProviderByPlatform(channel.Platform);
            if (provider == null)
            {
                return BadRequest(new { message = $"No provider found for platform {channel.Platform}" });
            }

            // Get all playlists from the channel (this would need to be implemented in the provider)
            // For now, we'll just return playlists already in our database
            var predicate = PredicateBuilder.New<Playlist>(v => v.ChannelId == channelId);

            if (!showIgnored)
            {
                predicate = predicate.And(p => !p.IsIgnored);
            }

            var options = new SearchOptions<Playlist>
            {
                CancellationToken = HttpContext.RequestAborted,
                Query = predicate,
                OrderBy = query => query
                    .OrderByDescending(p => p.SubscribedAt)
                    .ThenBy(p => p.Name)
            };

            var playlists = await _playlistRepository.FindAsync(options, p => new
            {
                p.Id,
                p.PlaylistId,
                p.Name,
                p.Description,
                p.Url,
                p.ThumbnailUrl,
                p.VideoCount,
                p.SubscribedAt,
                p.LastChecked,
                p.IsIgnored,
                IsSubscribed = p.SubscribedAt != default
            });

            return Ok(new { playlists });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving playlists for channel {ChannelId}", channelId);
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

            if (request.PlaylistIds == null || request.PlaylistIds.Count == 0)
            {
                return BadRequest(new { message = "No playlist IDs provided" });
            }

            var channel = await _channelRepository.FindOneAsync(channelId);
            if (channel == null)
            {
                return NotFound(new { message = "Channel not found" });
            }

            string? userId = _userContext.GetCurrentUserId();

            int subscribedCount = 0;
            foreach (int playlistId in request.PlaylistIds)
            {
                var playlist = await _playlistRepository.FindOneAsync(playlistId);
                if (playlist != null && playlist.ChannelId == channelId)
                {
                    // Update SubscribedAt if not already subscribed
                    if (playlist.SubscribedAt == DateTime.MinValue)
                    {
                        playlist.SubscribedAt = DateTime.UtcNow;
                        await _playlistRepository.UpdateAsync(playlist);
                    }

                    // Check if user already subscribed
                    var existingSubscription = await _userPlaylistRepository.FindOneAsync(new SearchOptions<UserPlaylist>
                    {
                        Query = up => up.UserId == userId && up.PlaylistId == playlistId
                    });

                    if (existingSubscription == null)
                    {
                        // Create user subscription
                        await _userPlaylistRepository.InsertAsync(new UserPlaylist
                        {
                            UserId = userId!,
                            PlaylistId = playlistId,
                            SubscribedAt = DateTime.UtcNow
                        });
                    }

                    // Queue sync job for the playlist
                    _backgroundJobClient.Enqueue<PlaylistSyncJob>(job =>
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
            _logger.LogError(ex, "Error subscribing to playlists for channel {ChannelId}", channelId);
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

            string? userId = _userContext.GetCurrentUserId();

            var playlists = await _playlistRepository.FindAsync(new SearchOptions<Playlist>
            {
                Query = p => p.ChannelId == channelId && !p.IsIgnored
            });

            if (playlists.Count == 0)
            {
                return Ok(new { message = "No playlists available to subscribe", subscribedCount = 0 });
            }

            foreach (var playlist in playlists)
            {
                // Update SubscribedAt if not already subscribed
                if (playlist.SubscribedAt == DateTime.MinValue)
                {
                    playlist.SubscribedAt = DateTime.UtcNow;
                    await _playlistRepository.UpdateAsync(playlist);
                }

                // Check if user already subscribed
                var existingSubscription = await _userPlaylistRepository.FindOneAsync(new SearchOptions<UserPlaylist>
                {
                    Query = up => up.UserId == userId && up.PlaylistId == playlist.Id
                });

                if (existingSubscription == null)
                {
                    // Create user subscription
                    await _userPlaylistRepository.InsertAsync(new UserPlaylist
                    {
                        UserId = userId!,
                        PlaylistId = playlist.Id,
                        SubscribedAt = DateTime.UtcNow
                    });
                }

                _backgroundJobClient.Enqueue<PlaylistSyncJob>(job =>
                    job.ExecuteAsync(playlist.Id, CancellationToken.None));
            }

            return Ok(new
            {
                message = $"Queued sync for {playlists.Count} playlist(s)",
                subscribedCount = playlists.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subscribing to all playlists for channel {ChannelId}", channelId);
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

            var playlist = await _playlistRepository.FindOneAsync(new SearchOptions<Playlist>
            {
                Query = p => p.Id == playlistId && p.ChannelId == channelId
            });

            if (playlist == null)
            {
                return NotFound(new { message = "Playlist not found" });
            }

            playlist.IsIgnored = request.IsIgnored;
            await _playlistRepository.UpdateAsync(playlist);

            return Ok(new
            {
                message = request.IsIgnored ? "Playlist ignored" : "Playlist unignored",
                isIgnored = playlist.IsIgnored
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling ignore status for playlist {PlaylistId}", playlistId);
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

            var channel = await _channelRepository.FindOneAsync(channelId);
            if (channel == null)
            {
                return NotFound(new { message = "Channel not found" });
            }

            // Get metadata provider
            var provider = _metadataProviderFactory.GetProviderByPlatform(channel.Platform);
            if (provider == null)
            {
                return BadRequest(new { message = $"No provider found for platform {channel.Platform}" });
            }

            // Fetch playlists from YouTube
            var playlistMetadataList = await provider.GetChannelPlaylistsAsync(channel.Url);

            _logger.LogInformation("Found {Count} playlists for channel {ChannelId}", playlistMetadataList.Count, channelId);

            int newPlaylistsCount = 0;
            var existingPlaylists = await _playlistRepository.FindAsync(new SearchOptions<Playlist>
            {
                Query = p => p.ChannelId == channelId
            });

            var existingPlaylistIds = existingPlaylists.Select(p => p.PlaylistId).ToHashSet();

            foreach (var playlistMetadata in playlistMetadataList)
            {
                if (existingPlaylistIds.Contains(playlistMetadata.PlaylistId))
                {
                    // Update existing playlist
                    var existingPlaylist = existingPlaylists.First(p => p.PlaylistId == playlistMetadata.PlaylistId);
                    existingPlaylist.Name = playlistMetadata.Name;
                    existingPlaylist.Description = playlistMetadata.Description;
                    existingPlaylist.Url = playlistMetadata.Url;
                    existingPlaylist.ThumbnailUrl = playlistMetadata.ThumbnailUrl;
                    existingPlaylist.VideoCount = playlistMetadata.VideoCount;

                    await _playlistRepository.UpdateAsync(existingPlaylist);
                }
                else
                {
                    // Create new playlist entry
                    var newPlaylist = new Playlist
                    {
                        PlaylistId = playlistMetadata.PlaylistId,
                        Name = playlistMetadata.Name,
                        Description = playlistMetadata.Description,
                        Url = playlistMetadata.Url,
                        ThumbnailUrl = playlistMetadata.ThumbnailUrl,
                        Platform = playlistMetadata.Platform,
                        VideoCount = playlistMetadata.VideoCount,
                        SubscribedAt = DateTime.MinValue, // Not subscribed yet
                        IsIgnored = false,
                        ChannelId = channelId
                    };

                    await _playlistRepository.InsertAsync(newPlaylist);
                    newPlaylistsCount++;
                }
            }

            return Ok(new
            {
                message = $"Refreshed playlists. Found {playlistMetadataList.Count} total, {newPlaylistsCount} new",
                totalCount = playlistMetadataList.Count,
                newCount = newPlaylistsCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing playlists for channel {ChannelId}", channelId);
            return StatusCode(500, new { message = "An error occurred while refreshing playlists" });
        }
    }
}

public class SubscribePlaylistsRequest
{
    public List<int> PlaylistIds { get; set; } = [];
}

public class IgnorePlaylistRequest
{
    public bool IsIgnored { get; set; }
}
