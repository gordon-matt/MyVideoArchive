using MyVideoArchive.Models.Api;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for managing channel videos (available, download, ignore operations)
/// </summary>
[Authorize]
[ApiController]
[Route("api/channels/{channelId}/videos")]
public class ChannelVideosApiController : ControllerBase
{
    private readonly IChannelService channelService;
    private readonly IVideoService videoService;

    public ChannelVideosApiController(
        IChannelService channelService,
        IVideoService videoService)
    {
        this.channelService = channelService;
        this.videoService = videoService;
    }

    [HttpGet("downloaded")]
    public async Task<IActionResult> GetDownloadedVideos(
        int channelId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 24,
        [FromQuery] string? search = null,
        [FromQuery] int? playlistId = null)
    {
        var result = await channelService.GetDownloadedVideosAsync(
            channelId,
            page,
            pageSize,
            search,
            playlistId,
            HttpContext.RequestAborted);

        return result.ToActionResult(this, value => Ok(new
        {
            videos = value,
            pagination = new
            {
                currentPage = page,
                pageSize,
                totalCount = value.ItemCount,
                totalPages = value.PageCount
            }
        }));
    }

    [HttpGet("available")]
    public async Task<IActionResult> GetAvailableVideos(
        int channelId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool showIgnored = false,
        [FromQuery] string? search = null)
    {
        var result = await channelService.GetAvailableVideosAsync(
            channelId,
            page,
            pageSize,
            showIgnored,
            search,
            HttpContext.RequestAborted);

        return result.ToActionResult(this, value => Ok(new
        {
            videos = value,
            pagination = new
            {
                currentPage = page,
                pageSize,
                totalCount = value.ItemCount,
                totalPages = value.PageCount
            }
        }));
    }

    [HttpPost("download")]
    public async Task<IActionResult> DownloadVideos(int channelId, [FromBody] DownloadVideosRequest request)
    {
        var result = await channelService.DownloadVideosAsync(channelId, request);

        return result.ToActionResult(this, value => Ok(new
        {
            message = $"Queued {value} video(s) for download",
            queuedCount = value
        }));
    }

    [HttpPost("download-all")]
    public async Task<IActionResult> DownloadAllVideos(int channelId)
    {
        var result = await channelService.DownloadAllVideosAsync(channelId);

        return result.ToActionResult(this, value => value == 0
            ? Ok(new { message = "No videos available to download", queuedCount = 0 })
            : Ok(new
            {
                message = $"Queued {value} video(s) for download",
                queuedCount = value
            }));
    }

    [HttpDelete("{videoId}/file")]
    public async Task<IActionResult> DeleteVideoFile(int channelId, int videoId)
    {
        var result = await videoService.DeleteVideoFileAsync(channelId, videoId);

        return result.ToActionResult(this, () => Ok(new
        {
            message = "Video file deleted successfully"
        }));
    }

    [HttpPut("{videoId}/ignore")]
    public async Task<IActionResult> ToggleIgnore(int channelId, int videoId, [FromBody] IgnoreVideoRequest request)
    {
        var result = await videoService.ToggleIgnoreAsync(channelId, videoId, request);

        return result.ToActionResult(this, value => Ok(new
        {
            message = value ? "Video ignored" : "Video unignored",
            isIgnored = value
        }));
    }
}