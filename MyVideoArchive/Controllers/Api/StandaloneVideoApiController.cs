using MyVideoArchive.Models.Api;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for adding standalone videos and retrieving standalone video info
/// </summary>
[Authorize]
[ApiController]
[Route("api/videos")]
public class StandaloneVideoApiController : ControllerBase
{
    private readonly IVideoService videoService;

    public StandaloneVideoApiController(IVideoService videoService)
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
}