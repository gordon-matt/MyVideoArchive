using MyVideoArchive.Models.Api;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for video streaming and operations
/// </summary>
[Authorize]
[ApiController]
[Route("api/videos")]
public class VideosApiController : ControllerBase
{
    private readonly IVideoService videoService;

    public VideosApiController(IVideoService videoService)
    {
        this.videoService = videoService;
    }

    /// <summary>
    /// Add a standalone video by URL. Fetches metadata, creates channel if needed,
    /// tags as standalone, and queues for download.
    /// </summary>
    [HttpPost("standalone")]
    public async Task<IActionResult> AddStandaloneVideo([FromBody] AddStandaloneVideoRequest request)
    {
        var result = await videoService.AddStandaloneVideoAsync(request, HttpContext.RequestAborted);

        return result.ToActionResult(this, value => Ok(new
        {
            videoId = value.VideoId,
            title = value.Title,
            channelId = value.ChannelId,
            channelName = value.ChannelName,
            isAlreadyDownloaded = value.IsAlreadyDownloaded
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

    /// <summary>
    /// Get standalone status info for a video (for the banner on the details page)
    /// </summary>
    [HttpGet("{videoId}/standalone-info")]
    public async Task<IActionResult> GetStandaloneInfo(int videoId)
    {
        var result = await videoService.GetStandaloneInfoAsync(videoId);

        return result.ToActionResult(this, value => Ok(new
        {
            isStandalone = value.IsStandalone,
            channelVideoCount = value.ChannelVideoCount,
            channelId = value.ChannelId,
            channelName = value.ChannelName,
            channelUrl = value.ChannelUrl,
            channelPlatformId = value.ChannelPlatformId,
            channelPlatform = value.ChannelPlatform,
            isSubscribed = value.IsSubscribed
        }));
    }

    /// <summary>
    /// Get playlists that contain a specific video
    /// </summary>
    [HttpGet("{videoId}/playlists")]
    public async Task<IActionResult> GetVideoPlaylists(int videoId)
    {
        var result = await videoService.GetVideoPlaylistsAsync(videoId);

        return result.ToActionResult(this, value => Ok(new { playlists = value.Playlists }));
    }

    /// <summary>
    /// Get paginated videos accessible to the current user, with optional filters
    /// </summary>
    [HttpGet("search")]
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
    /// Stream a video file
    /// </summary>
    [HttpGet("{videoId}/stream")]
    public async Task<IActionResult> StreamVideo(int videoId)
    {
        var result = await videoService.GetVideoStreamInfoAsync(videoId);

        return result.ToActionResult(this, info =>
        {
            var fileStream = new FileStream(info.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(fileStream, info.ContentType, enableRangeProcessing: true);
        });
    }
}