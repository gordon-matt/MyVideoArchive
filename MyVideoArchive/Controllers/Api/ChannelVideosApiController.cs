using Ardalis.Result;
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
            x => new
            {
                x.Id,
                x.VideoId,
                x.Title,
                x.Url,
                x.ThumbnailUrl,
                x.Duration,
                x.UploadDate,
                x.DownloadedAt
            },
            page,
            pageSize,
            search,
            playlistId,
            HttpContext.RequestAborted);

        return result.IsSuccess
            ? Ok(new
            {
                videos = result.Value,
                pagination = new
                {
                    currentPage = page,
                    pageSize,
                    totalCount = result.Value.ItemCount,
                    totalPages = result.Value.PageCount
                }
            })
            : result.Status switch
            {
                ResultStatus.Forbidden => Forbid(),
                ResultStatus.NotFound => NotFound(new { message = "Channel not found" }),
                _ => StatusCode(500, new { message = "An error occurred while retrieving videos" })
            };
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
            x => new
            {
                x.Id,
                x.VideoId,
                x.Title,
                x.Description,
                x.Url,
                x.ThumbnailUrl,
                x.Duration,
                x.UploadDate,
                x.ViewCount,
                x.LikeCount,
                x.DownloadedAt,
                x.IsIgnored,
                IsDownloaded = x.DownloadedAt != null
            },
            page,
            pageSize,
            showIgnored,
            search,
            HttpContext.RequestAborted);

        return result.IsSuccess
            ? Ok(new
            {
                videos = result.Value,
                pagination = new
                {
                    currentPage = page,
                    pageSize,
                    totalCount = result.Value.ItemCount,
                    totalPages = result.Value.PageCount
                }
            })
            : result.Status switch
            {
                ResultStatus.Forbidden => Forbid(),
                ResultStatus.NotFound => NotFound(new { message = "Channel not found" }),
                _ => StatusCode(500, new { message = "An error occurred while retrieving videos" })
            };
    }

    [HttpPost("download")]
    public async Task<IActionResult> DownloadVideos(int channelId, [FromBody] DownloadVideosRequest request)
    {
        var result = await channelService.DownloadVideosAsync(channelId, request);

        return result.IsSuccess
            ? Ok(new
            {
                message = $"Queued {result.Value} video(s) for download",
                queuedCount = result.Value
            })
            : result.Status switch
            {
                ResultStatus.Forbidden => Forbid(),
                ResultStatus.Invalid => BadRequest(new { message = result.ValidationErrors?.First()?.ErrorMessage }),
                ResultStatus.NotFound => NotFound(new { message = "No videos found" }),
                _ => StatusCode(500, new { message = "An error occurred while queueing downloads" })
            };
    }

    [HttpPost("download-all")]
    public async Task<IActionResult> DownloadAllVideos(int channelId)
    {
        var result = await channelService.DownloadAllVideosAsync(channelId);

        return result.IsSuccess
            ? result.Value == 0
                ? Ok(new { message = "No videos available to download", queuedCount = 0 })
                : Ok(new
                {
                    message = $"Queued {result.Value} video(s) for download",
                    queuedCount = result.Value
                })
            : result.Status switch
            {
                ResultStatus.Forbidden => Forbid(),
                ResultStatus.NotFound => NotFound(new { message = "No videos found" }),
                _ => StatusCode(500, new { message = "An error occurred while queueing downloads" })
            };
    }

    [HttpDelete("{videoId}/file")]
    public async Task<IActionResult> DeleteVideoFile(int channelId, int videoId)
    {
        var result = await videoService.DeleteVideoFileAsync(channelId, videoId);

        return result.IsSuccess
            ? Ok(new { message = "Video file deleted successfully" })
            : result.Status switch
            {
                ResultStatus.Forbidden => Forbid(),
                ResultStatus.NotFound => NotFound(new { message = "Video not found" }),
                _ => StatusCode(500, new { message = "An error occurred while deleting the video file" })
            };
    }

    [HttpPut("{videoId}/ignore")]
    public async Task<IActionResult> ToggleIgnore(int channelId, int videoId, [FromBody] IgnoreVideoRequest request)
    {
        var result = await videoService.ToggleIgnoreAsync(channelId, videoId, request);

        return result.IsSuccess
            ? Ok(new
            {
                message = result.Value ? "Video ignored" : "Video unignored",
                isIgnored = result.Value
            })
            : result.Status switch
            {
                ResultStatus.Forbidden => Forbid(),
                ResultStatus.NotFound => NotFound(new { message = "Video not found" }),
                _ => StatusCode(500, new { message = "An error occurred while updating video status" })
            };
    }
}