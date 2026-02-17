using Extenso.Data.Entity;
using Microsoft.AspNetCore.Mvc;
using MyVideoArchive.Data.Entities;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for video streaming and operations
/// </summary>
[ApiController]
[Route("api/videos")]
public class VideosApiController : ControllerBase
{
    private readonly IRepository<Video> _videoRepository;
    private readonly ILogger<VideosApiController> _logger;

    public VideosApiController(
        IRepository<Video> videoRepository,
        ILogger<VideosApiController> logger)
    {
        _videoRepository = videoRepository;
        _logger = logger;
    }

    /// <summary>
    /// Stream a video file
    /// </summary>
    [HttpGet("{videoId}/stream")]
    public async Task<IActionResult> StreamVideo(int videoId)
    {
        try
        {
            var video = await _videoRepository.FindOneAsync(videoId);

            if (video == null)
            {
                _logger.LogWarning("Video with ID {VideoId} not found", videoId);
                return NotFound(new { message = "Video not found" });
            }

            if (string.IsNullOrEmpty(video.FilePath) || !System.IO.File.Exists(video.FilePath))
            {
                _logger.LogWarning("Video file not found for video ID {VideoId} at path {FilePath}", videoId, video.FilePath);
                return NotFound(new { message = "Video file not found" });
            }

            var fileStream = new FileStream(video.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var fileExtension = Path.GetExtension(video.FilePath).ToLowerInvariant();
            
            // Determine content type based on file extension
            var contentType = fileExtension switch
            {
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".mkv" => "video/x-matroska",
                ".avi" => "video/x-msvideo",
                ".mov" => "video/quicktime",
                ".flv" => "video/x-flv",
                _ => "application/octet-stream"
            };

            _logger.LogInformation("Streaming video {VideoId} from {FilePath}", videoId, video.FilePath);

            // Enable range requests for seeking support
            return File(fileStream, contentType, enableRangeProcessing: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming video {VideoId}", videoId);
            return StatusCode(500, new { message = "An error occurred while streaming the video" });
        }
    }
}
