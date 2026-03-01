using Extenso;
using MyVideoArchive.Models.Api;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for playlist-specific operations (reordering, hiding, etc.)
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
    private readonly IRepository<UserPlaylistVideo> userPlaylistVideoRepository;

    public PlaylistOperationsApiController(
        ILogger<PlaylistOperationsApiController> logger,
        IUserContextService userContextService,
        IRepository<PlaylistVideo> playlistVideoRepository,
        IRepository<UserPlaylist> userPlaylistRepository,
        IRepository<UserPlaylistVideo> userPlaylistVideoRepository)
    {
        this.logger = logger;
        this.userContextService = userContextService;
        this.playlistVideoRepository = playlistVideoRepository;
        this.userPlaylistRepository = userPlaylistRepository;
        this.userPlaylistVideoRepository = userPlaylistVideoRepository;
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

                userPlaylist = new UserPlaylist
                {
                    UserId = userId,
                    PlaylistId = playlistId,
                    SubscribedAt = DateTime.UtcNow
                };
                await userPlaylistRepository.InsertAsync(userPlaylist);
            }

            // Persist the user's order preference
            userPlaylist.UseCustomOrder = request.UseCustomOrder;
            await userPlaylistRepository.UpdateAsync(userPlaylist);

            if (request.UseCustomOrder && request.VideoOrders is not null)
            {
                // Fetch all existing records so we can preserve hidden-video entries
                var existingRecords = await userPlaylistVideoRepository.FindAsync(new SearchOptions<UserPlaylistVideo>
                {
                    Query = x =>
                        x.UserId == userId &&
                        x.PlaylistId == playlistId
                });

                var hiddenVideoIds = existingRecords
                    .Where(x => x.IsHidden)
                    .Select(x => x.VideoId)
                    .ToHashSet();

                // Delete all existing records (hidden ones will be re-inserted below)
                foreach (var record in existingRecords)
                {
                    await userPlaylistVideoRepository.DeleteAsync(record);
                }

                // Re-insert visible videos with the new custom order
                foreach (var videoOrder in request.VideoOrders)
                {
                    await userPlaylistVideoRepository.InsertAsync(new UserPlaylistVideo
                    {
                        UserId = userId,
                        PlaylistId = playlistId,
                        VideoId = videoOrder.VideoId,
                        CustomOrder = videoOrder.Order,
                        IsHidden = false
                    });
                }

                // Re-insert hidden videos, preserving their hidden status
                foreach (var hiddenVideoId in hiddenVideoIds)
                {
                    await userPlaylistVideoRepository.InsertAsync(new UserPlaylistVideo
                    {
                        UserId = userId,
                        PlaylistId = playlistId,
                        VideoId = hiddenVideoId,
                        CustomOrder = 0,
                        IsHidden = true
                    });
                }
            }

            return Ok(new { message = "Custom order saved successfully" });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error saving custom order for playlist {PlaylistId}", playlistId);
            }

            return StatusCode(500, new { message = "An error occurred while saving custom order" });
        }
    }

    /// <summary>
    /// Get the current order setting and preference for a playlist
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

            var userPlaylist = await userPlaylistRepository.FindOneAsync(new SearchOptions<UserPlaylist>
            {
                Query = x =>
                    x.UserId == userId &&
                    x.PlaylistId == playlistId
            });

            bool useCustomOrder = userPlaylist?.UseCustomOrder ?? false;

            // hasCustomOrder tells the client whether order records already exist in DB,
            // so it can decide whether to save current order or reload saved order.
            var orderRecords = await userPlaylistVideoRepository.FindAsync(new SearchOptions<UserPlaylistVideo>
            {
                Query = x =>
                    x.UserId == userId &&
                    x.PlaylistId == playlistId &&
                    x.CustomOrder > 0
            });

            bool hasCustomOrder = orderRecords.Count > 0;

            return Ok(new { hasCustomOrder, useCustomOrder });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error getting order setting for playlist {PlaylistId}", playlistId);
            }

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

            var videoOrders = (await userPlaylistVideoRepository.FindAsync(new SearchOptions<UserPlaylistVideo>
            {
                Query = x =>
                    x.UserId == userId &&
                    x.PlaylistId == playlistId &&
                    x.CustomOrder > 0,

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
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error getting custom order for playlist {PlaylistId}", playlistId);
            }

            return StatusCode(500, new { message = "An error occurred while getting custom order" });
        }
    }

    /// <summary>
    /// Get videos for a playlist with proper ordering and hidden-video awareness
    /// </summary>
    [HttpGet("{playlistId}/videos")]
    public async Task<IActionResult> GetPlaylistVideos(int playlistId, bool useCustomOrder = false, bool showHidden = false)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            IEnumerable<PlaylistVideo> playlistVideos = await playlistVideoRepository.FindAsync(new SearchOptions<PlaylistVideo>
            {
                Query = x => x.PlaylistId == playlistId,
                Include = query => query
                    .Include(x => x.Video)
                        .ThenInclude(x => x.Channel),

                OrderBy = query => query
                    .OrderBy(x => x.Order)
            });

            // Fetch all user-specific per-video settings in one query
            var userPlaylistVideos = await userPlaylistVideoRepository.FindAsync(new SearchOptions<UserPlaylistVideo>
            {
                Query = x =>
                    x.UserId == userId &&
                    x.PlaylistId == playlistId
            });

            var hiddenVideoIds = userPlaylistVideos
                .Where(x => x.IsHidden)
                .Select(x => x.VideoId)
                .ToHashSet();

            bool hasCustomOrder = userPlaylistVideos.Any(x => x.CustomOrder > 0);

            if (useCustomOrder && hasCustomOrder)
            {
                var orderMap = userPlaylistVideos
                    .Where(x => x.CustomOrder > 0)
                    .ToDictionary(x => x.VideoId, x => x.CustomOrder);

                playlistVideos = playlistVideos
                    .OrderBy(x => orderMap.TryGetValue(x.VideoId, out var order) ? order : int.MaxValue)
                    .ToList();
            }

            if (!showHidden)
            {
                playlistVideos = playlistVideos
                    .Where(x => !hiddenVideoIds.Contains(x.VideoId))
                    .ToList();
            }

            var videos = playlistVideos.Select(x => new
            {
                x.Video.Id,
                x.Video.VideoId,
                Title = x.Video.Title.In(Constants.PrivateVideoTitle, Constants.DeletedVideoTitle)
                    ? $"{x.Video.Title} - {x.Video.VideoId}"
                    : x.Video.Title,
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
                },
                IsHidden = hiddenVideoIds.Contains(x.VideoId)
            }).ToList();

            return Ok(new { videos });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error getting videos for playlist {PlaylistId}", playlistId);
            }

            return StatusCode(500, new { message = "An error occurred while getting playlist videos" });
        }
    }

    /// <summary>
    /// Set the hidden/visible status of a video within a playlist for the current user
    /// </summary>
    [HttpPut("{playlistId}/videos/{videoId}/hidden")]
    public async Task<IActionResult> SetVideoHidden(int playlistId, int videoId, [FromBody] SetVideoHiddenRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var existing = await userPlaylistVideoRepository.FindOneAsync(new SearchOptions<UserPlaylistVideo>
            {
                Query = x =>
                    x.UserId == userId &&
                    x.PlaylistId == playlistId &&
                    x.VideoId == videoId
            });

            if (existing is not null)
            {
                existing.IsHidden = request.IsHidden;
                await userPlaylistVideoRepository.UpdateAsync(existing);
            }
            else
            {
                await userPlaylistVideoRepository.InsertAsync(new UserPlaylistVideo
                {
                    UserId = userId,
                    PlaylistId = playlistId,
                    VideoId = videoId,
                    CustomOrder = 0,
                    IsHidden = request.IsHidden
                });
            }

            return Ok(new { message = request.IsHidden ? "Video hidden" : "Video unhidden" });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error setting hidden status for video {VideoId} in playlist {PlaylistId}", videoId, playlistId);
            }

            return StatusCode(500, new { message = "An error occurred while updating video visibility" });
        }
    }
}