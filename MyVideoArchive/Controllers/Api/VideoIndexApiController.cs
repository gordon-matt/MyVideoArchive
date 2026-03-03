namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for the user-facing video index (all accessible downloaded videos)
/// </summary>
[Authorize]
[ApiController]
[Route("api/video-index")]
public class VideoIndexApiController : ControllerBase
{
    private readonly IVideoService videoService;

    public VideoIndexApiController(IVideoService videoService)
    {
        this.videoService = videoService;
    }

    /// <summary>
    /// Get paginated videos accessible to the current user, with optional filters
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetVideos(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 60,
        [FromQuery] string? search = null,
        [FromQuery] int? channelId = null,
        [FromQuery] string? tagFilter = null)
    {
        var result = await videoService.GetVideosAsync(
            page,
            pageSize,
            search,
            channelId,
            tagFilter,
            HttpContext.RequestAborted);

        return result.ToActionResult(this, value => Ok(new
        {
            videos = value.Videos,
            pagination = new
            {
                currentPage = value.CurrentPage,
                pageSize = value.PageSize,
                totalCount = value.TotalCount,
                totalPages = value.TotalPages
            }
        }));
    }

    /// <summary>
    /// Get channels accessible to the current user (for the channel filter dropdown)
    /// </summary>
    [HttpGet("channels")]
    public async Task<IActionResult> GetAccessibleChannels()
    {
        var result = await videoService.GetAccessibleChannelsAsync(HttpContext.RequestAborted);

        return result.ToActionResult(this, value => Ok(new { channels = value.Channels }));
    }
}