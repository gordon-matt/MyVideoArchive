namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for playlist-specific operations (reordering, etc.)
/// </summary>
[Authorize]
[ApiController]
[Route("api/playlists")]
public class PlaylistOperationsApiController : ControllerBase
{
    private readonly ILogger<PlaylistOperationsApiController> logger;
    private readonly IUserContextService userContextService;
    private readonly IRepository<PlaylistVideo> playlistVideoRepository;
    private readonly IRepository<UserPlaylist> userPlaylistRepository;
    private readonly IRepository<UserVideoOrder> userVideoOrderRepository;

    public PlaylistOperationsApiController(
        ILogger<PlaylistOperationsApiController> logger,
        IUserContextService userContextService,
        IRepository<PlaylistVideo> playlistVideoRepository,
        IRepository<UserPlaylist> userPlaylistRepository,
        IRepository<UserVideoOrder> userVideoOrderRepository)
    {
        this.logger = logger;
        this.userContextService = userContextService;
        this.playlistVideoRepository = playlistVideoRepository;
        this.userPlaylistRepository = userPlaylistRepository;
        this.userVideoOrderRepository = userVideoOrderRepository;
    }

    /// <summary>
    /// Save custom video order for a playlist
    /// </summary>
    [HttpPost("{playlistId}/reorder")]
    public async Task<IActionResult> SaveCustomOrder(int playlistId, [FromBody] ReorderVideosRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            // Check if user has access to this playlist
            var userPlaylist = await userPlaylistRepository.FindOneAsync(new SearchOptions<UserPlaylist>
            {
                Query = x =>
                    x.UserId == userId &&
                    x.PlaylistId == playlistId
            });

            if (userPlaylist is null)
            {
                if (!userContextService.IsAdministrator())
                {
                    return Forbid();
                }

                // If no UserPlaylist exists, create one
                userPlaylist = new UserPlaylist
                {
                    UserId = userId,
                    PlaylistId = playlistId,
                    SubscribedAt = DateTime.UtcNow
                };
                await userPlaylistRepository.InsertAsync(userPlaylist);
            }

            // Save the custom order setting
            // If using custom order, save the video orders
            if (request.UseCustomOrder && request.VideoOrders is not null)
            {
                // Delete existing custom orders for this user/playlist
                var existingOrders = await userVideoOrderRepository.FindAsync(new SearchOptions<UserVideoOrder>
                {
                    Query = x =>
                        x.UserId == userId &&
                        x.PlaylistId == playlistId
                });

                foreach (var existingOrder in existingOrders)
                {
                    await userVideoOrderRepository.DeleteAsync(existingOrder);
                }

                // Insert new custom orders
                foreach (var videoOrder in request.VideoOrders)
                {
                    await userVideoOrderRepository.InsertAsync(new UserVideoOrder
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
            logger.LogError(ex, "Error saving custom order for playlist {PlaylistId}", playlistId);
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
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            // Check if user has custom order by seeing if UserVideoOrder records exist
            var customOrders = await userVideoOrderRepository.FindAsync(new SearchOptions<UserVideoOrder>
            {
                Query = x =>
                    x.UserId == userId &&
                    x.PlaylistId == playlistId
            });

            bool useCustomOrder = customOrders.Count > 0;

            return Ok(new { useCustomOrder });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting order setting for playlist {PlaylistId}", playlistId);
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
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var videoOrders = (await userVideoOrderRepository.FindAsync(new SearchOptions<UserVideoOrder>
            {
                Query = x =>
                    x.UserId == userId &&
                    x.PlaylistId == playlistId,

                OrderBy = query => query
                    .OrderBy(x => x.CustomOrder)
            }, x => new
            {
                videoId = x.VideoId,
                order = x.CustomOrder
            })).ToList();

            return Ok(new { videoOrders });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting custom order for playlist {PlaylistId}", playlistId);
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
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            // Get all PlaylistVideo records to get original order
            IEnumerable<PlaylistVideo> playlistVideos = await playlistVideoRepository.FindAsync(new SearchOptions<PlaylistVideo>
            {
                Query = x => x.PlaylistId == playlistId,

                Include = query => query
                    .Include(x => x.Video)
                        .ThenInclude(x => x.Channel),

                OrderBy = query => query
                    .OrderBy(x => x.Order)
            });

            // Check if user has custom order
            var customOrders = await userVideoOrderRepository.FindAsync(new SearchOptions<UserVideoOrder>
            {
                Query = x =>
                    x.UserId == userId &&
                    x.PlaylistId == playlistId
            });

            bool hasCustomOrder = customOrders.Count > 0;
            if (useCustomOrder && hasCustomOrder)
            {
                // Sort by custom order
                var orderMap = customOrders.ToDictionary(key => key.VideoId, val => val.CustomOrder);

                playlistVideos = playlistVideos
                    .OrderBy(x => orderMap.ContainsKey(x.VideoId) ? orderMap[x.VideoId] : 999999)
                    .ToList();
            }

            // Project to anonymous objects to avoid circular reference
            var videos = playlistVideos.Select(x => new
            {
                x.Video.Id,
                x.Video.VideoId,
                x.Video.Title,
                x.Video.Description,
                x.Video.Url,
                x.Video.ThumbnailUrl,
                x.Video.Duration,
                x.Video.UploadDate,
                x.Video.ViewCount,
                x.Video.LikeCount,
                x.Video.DownloadedAt,
                x.Video.IsIgnored,
                x.Video.IsQueued,
                x.Video.ChannelId,
                Channel = new
                {
                    x.Video.Channel.Id,
                    x.Video.Channel.Name
                }
            }).ToList();

            return Ok(new { videos });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting videos for playlist {PlaylistId}", playlistId);
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