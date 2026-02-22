namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for tracking user video watch state
/// </summary>
[Authorize]
[ApiController]
[Route("api/user/videos")]
public class UserVideoApiController : ControllerBase
{
    private readonly ILogger<UserVideoApiController> logger;
    private readonly IUserContextService userContextService;
    private readonly IRepository<UserVideo> userVideoRepository;

    public UserVideoApiController(
        ILogger<UserVideoApiController> logger,
        IUserContextService userContextService,
        IRepository<UserVideo> userVideoRepository)
    {
        this.logger = logger;
        this.userContextService = userContextService;
        this.userVideoRepository = userVideoRepository;
    }

    /// <summary>
    /// Returns the IDs of videos (from the supplied list) that the current user has watched
    /// </summary>
    [HttpGet("watched")]
    public async Task<IActionResult> GetWatchedVideoIds([FromQuery] int[] videoIds)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            if (videoIds.IsNullOrEmpty())
            {
                return Ok(new { watchedIds = Array.Empty<int>() });
            }

            var watchedIds = await userVideoRepository.FindAsync(
                new SearchOptions<UserVideo>
                {
                    Query = x =>
                        x.UserId == userId &&
                        videoIds.Contains(x.VideoId) &&
                        x.Watched
                },
                x => x.VideoId);

            return Ok(new { watchedIds });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving watched video IDs");
            }

            return StatusCode(500, new { message = "An error occurred while retrieving watched status" });
        }
    }

    /// <summary>
    /// Mark a video as watched for the current user
    /// </summary>
    [HttpPost("{videoId}/watched")]
    public async Task<IActionResult> MarkWatched(int videoId)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            await UpsertUserVideo(userId, videoId, watched: true);

            return Ok(new { message = "Video marked as watched" });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error marking video {VideoId} as watched", videoId);
            }

            return StatusCode(500, new { message = "An error occurred while updating watch status" });
        }
    }

    /// <summary>
    /// Mark a video as unwatched for the current user
    /// </summary>
    [HttpDelete("{videoId}/watched")]
    public async Task<IActionResult> MarkUnwatched(int videoId)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            await UpsertUserVideo(userId, videoId, watched: false);

            return Ok(new { message = "Video marked as unwatched" });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error marking video {VideoId} as unwatched", videoId);
            }

            return StatusCode(500, new { message = "An error occurred while updating watch status" });
        }
    }

    private async Task UpsertUserVideo(string userId, int videoId, bool watched)
    {
        var existing = await userVideoRepository.FindOneAsync(new SearchOptions<UserVideo>
        {
            Query = x => x.UserId == userId && x.VideoId == videoId
        });

        if (existing is not null)
        {
            existing.Watched = watched;
            await userVideoRepository.UpdateAsync(existing);
        }
        else
        {
            await userVideoRepository.InsertAsync(new UserVideo
            {
                UserId = userId,
                VideoId = videoId,
                Watched = watched
            });
        }
    }
}