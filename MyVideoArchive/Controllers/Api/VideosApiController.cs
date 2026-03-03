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
    /// Get playlists that contain a specific video
    /// </summary>
    [HttpGet("{videoId}/playlists")]
    public async Task<IActionResult> GetVideoPlaylists(int videoId)
    {
        var result = await videoService.GetVideoPlaylistsAsync(videoId);

        return result.ToActionResult(this, value => Ok(new { playlists = value.Playlists }));
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