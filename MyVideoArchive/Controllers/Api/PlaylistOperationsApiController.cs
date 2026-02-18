using Extenso.Data.Entity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyVideoArchive.Data;
using MyVideoArchive.Data.Entities;
using MyVideoArchive.Services;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for playlist-specific operations (reordering, etc.)
/// </summary>
[Authorize]
[ApiController]
[Route("api/playlists")]
public class PlaylistOperationsApiController : ControllerBase
{
    private readonly IRepository<Playlist> _playlistRepository;
    private readonly IRepository<UserPlaylist> _userPlaylistRepository;
    private readonly IRepository<UserVideoOrder> _userVideoOrderRepository;
    private readonly IUserContextService _userContext;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<PlaylistOperationsApiController> _logger;

    public PlaylistOperationsApiController(
        IRepository<Playlist> playlistRepository,
        IRepository<UserPlaylist> userPlaylistRepository,
        IRepository<UserVideoOrder> userVideoOrderRepository,
        IUserContextService userContext,
        ApplicationDbContext dbContext,
        ILogger<PlaylistOperationsApiController> logger)
    {
        _playlistRepository = playlistRepository;
        _userPlaylistRepository = userPlaylistRepository;
        _userVideoOrderRepository = userVideoOrderRepository;
        _userContext = userContext;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Save custom video order for a playlist
    /// </summary>
    [HttpPost("{playlistId}/reorder")]
    public async Task<IActionResult> SaveCustomOrder(int playlistId, [FromBody] ReorderVideosRequest request)
    {
        try
        {
            string? userId = _userContext.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            // Check if user has access to this playlist
            var userPlaylist = await _userPlaylistRepository.FindOneAsync(new SearchOptions<UserPlaylist>
            {
                Query = up => up.UserId == userId && up.PlaylistId == playlistId
            });

            if (userPlaylist == null && !_userContext.IsAdministrator())
            {
                return Forbid();
            }

            // If no UserPlaylist exists, create one
            if (userPlaylist == null)
            {
                userPlaylist = new UserPlaylist
                {
                    UserId = userId,
                    PlaylistId = playlistId,
                    SubscribedAt = DateTime.UtcNow
                };
                await _userPlaylistRepository.InsertAsync(userPlaylist);
            }

            // Save the custom order setting
            // If using custom order, save the video orders
            if (request.UseCustomOrder && request.VideoOrders != null)
            {
                // Delete existing custom orders for this user/playlist
                var existingOrders = await _userVideoOrderRepository.FindAsync(new SearchOptions<UserVideoOrder>
                {
                    Query = uvo => uvo.UserId == userId && uvo.PlaylistId == playlistId
                });

                foreach (var existingOrder in existingOrders)
                {
                    await _userVideoOrderRepository.DeleteAsync(existingOrder);
                }

                // Insert new custom orders
                foreach (var videoOrder in request.VideoOrders)
                {
                    await _userVideoOrderRepository.InsertAsync(new UserVideoOrder
                    {
                        UserId = userId,
                        PlaylistId = playlistId,
                        VideoId = videoOrder.VideoId,
                        CustomOrder = videoOrder.Order
                    });
                }
            }

            return Ok(new { message = "Custom order saved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving custom order for playlist {PlaylistId}", playlistId);
            return StatusCode(500, new { message = "An error occurred while saving custom order" });
        }
    }

    /// <summary>
    /// Get the current order setting for a playlist
    /// </summary>
    [HttpGet("{playlistId}/order-setting")]
    public async Task<IActionResult> GetOrderSetting(int playlistId)
    {
        try
        {
            string? userId = _userContext.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            // Check if user has custom order by seeing if UserVideoOrder records exist
            var customOrders = await _userVideoOrderRepository.FindAsync(new SearchOptions<UserVideoOrder>
            {
                Query = uvo => uvo.UserId == userId && uvo.PlaylistId == playlistId
            });

            bool useCustomOrder = customOrders.Count > 0;

            return Ok(new { useCustomOrder });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order setting for playlist {PlaylistId}", playlistId);
            return StatusCode(500, new { message = "An error occurred while getting order setting" });
        }
    }

    /// <summary>
    /// Get custom video order for a playlist
    /// </summary>
    [HttpGet("{playlistId}/custom-order")]
    public async Task<IActionResult> GetCustomOrder(int playlistId)
    {
        try
        {
            string? userId = _userContext.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var customOrders = await _userVideoOrderRepository.FindAsync(new SearchOptions<UserVideoOrder>
            {
                Query = uvo => uvo.UserId == userId && uvo.PlaylistId == playlistId,
                OrderBy = q => q.OrderBy(uvo => uvo.CustomOrder)
            });

            var videoOrders = customOrders.Select(co => new { videoId = co.VideoId, order = co.CustomOrder }).ToList();

            return Ok(new { videoOrders });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting custom order for playlist {PlaylistId}", playlistId);
            return StatusCode(500, new { message = "An error occurred while getting custom order" });
        }
    }

    /// <summary>
    /// Get videos for a playlist with proper ordering (original or custom)
    /// </summary>
    [HttpGet("{playlistId}/videos")]
    public async Task<IActionResult> GetPlaylistVideos(int playlistId, bool useCustomOrder = false)
    {
        try
        {
            string? userId = _userContext.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            // Get all VideoPlaylist records to get original order
            var videoPlaylists = await _dbContext.Set<VideoPlaylist>()
                .Where(vp => vp.PlaylistId == playlistId)
                .Include(vp => vp.Video)
                .ThenInclude(v => v.Channel)
                .OrderBy(vp => vp.Order)
                .ToListAsync();

            // Check if user has custom order
            var customOrders = await _userVideoOrderRepository.FindAsync(new SearchOptions<UserVideoOrder>
            {
                Query = uvo => uvo.UserId == userId && uvo.PlaylistId == playlistId
            });

            bool hasCustomOrder = customOrders.Count > 0;
            if (useCustomOrder && hasCustomOrder)
            {
                // Sort by custom order
                var orderMap = customOrders.ToDictionary(co => co.VideoId, co => co.CustomOrder);
                videoPlaylists = videoPlaylists
                    .OrderBy(vp => orderMap.ContainsKey(vp.VideoId) ? orderMap[vp.VideoId] : 999999)
                    .ToList();
            }

            // Project to anonymous objects to avoid circular reference
            var videos = videoPlaylists.Select(vp => new
            {
                vp.Video.Id,
                vp.Video.VideoId,
                vp.Video.Title,
                vp.Video.Description,
                vp.Video.Url,
                vp.Video.ThumbnailUrl,
                vp.Video.Duration,
                vp.Video.UploadDate,
                vp.Video.ViewCount,
                vp.Video.LikeCount,
                vp.Video.DownloadedAt,
                vp.Video.IsIgnored,
                vp.Video.IsQueued,
                vp.Video.ChannelId,
                Channel = new
                {
                    vp.Video.Channel.Id,
                    vp.Video.Channel.Name
                }
            }).ToList();

            return Ok(new { videos });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting videos for playlist {PlaylistId}", playlistId);
            return StatusCode(500, new { message = "An error occurred while getting playlist videos" });
        }
    }
}

public class ReorderVideosRequest
{
    public bool UseCustomOrder { get; set; }
    public List<VideoOrderItem>? VideoOrders { get; set; }
}

public class VideoOrderItem
{
    public int VideoId { get; set; }
    public int Order { get; set; }
}
