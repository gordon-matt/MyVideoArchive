namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for video streaming and operations
/// </summary>
[ApiController]
[Route("api/videos")]
public class VideosApiController : ControllerBase
{
    private readonly ILogger<VideosApiController> logger;
    private readonly IRepository<Video> videoRepository;

    public VideosApiController(
        ILogger<VideosApiController> logger,
        IRepository<Video> videoRepository)
    {
        this.logger = logger;
        this.videoRepository = videoRepository;
    }

    /// <summary>
    /// Stream a video file
    /// </summary>
    [HttpGet("{videoId}/stream")]
    public async Task<IActionResult> StreamVideo(int videoId)
    {
        try
        {
            var video = await videoRepository.FindOneAsync(videoId);

            if (video is null)
            {
                logger.LogWarning("Video with ID {VideoId} not found", videoId);
                return NotFound(new { message = "Video not found" });
            }

            if (string.IsNullOrEmpty(video.FilePath) || !System.IO.File.Exists(video.FilePath))
            {
                logger.LogWarning("Video file not found for video ID {VideoId} at path {FilePath}", videoId, video.FilePath);
                return NotFound(new { message = "Video file not found" });
            }

            var fileStream = new FileStream(video.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            string fileExtension = Path.GetExtension(video.FilePath).ToLowerInvariant();

            // Determine content type based on file extension
            string contentType = fileExtension switch
            {
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".mkv" => "video/x-matroska",
                ".avi" => "video/x-msvideo",
                ".mov" => "video/quicktime",
                ".flv" => "video/x-flv",
                _ => "application/octet-stream"
            };

            logger.LogInformation("Streaming video {VideoId} from {FilePath}", videoId, video.FilePath);

            // Enable range requests for seeking support
            return File(fileStream, contentType, enableRangeProcessing: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error streaming video {VideoId}", videoId);
            return StatusCode(500, new { message = "An error occurred while streaming the video" });
        }
    }
}