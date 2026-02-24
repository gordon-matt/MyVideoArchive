namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for video streaming and operations
/// </summary>
[Authorize]
[ApiController]
[Route("api/videos")]
public class VideosApiController : ControllerBase
{
    private readonly ILogger<VideosApiController> logger;
    private readonly IRepository<Video> videoRepository;
    private readonly IRepository<PlaylistVideo> playlistVideoRepository;

    public VideosApiController(
        ILogger<VideosApiController> logger,
        IRepository<Video> videoRepository,
        IRepository<PlaylistVideo> playlistVideoRepository)
    {
        this.logger = logger;
        this.videoRepository = videoRepository;
        this.playlistVideoRepository = playlistVideoRepository;
    }

    /// <summary>
    /// Get playlists that contain a specific video
    /// </summary>
    [HttpGet("{videoId}/playlists")]
    public async Task<IActionResult> GetVideoPlaylists(int videoId)
    {
        try
        {
            var playlistVideos = await playlistVideoRepository.FindAsync(new SearchOptions<PlaylistVideo>
            {
                Query = x => x.VideoId == videoId,
                Include = query => query.Include(x => x.Playlist)
            });

            var playlists = playlistVideos.Select(x => new
            {
                x.Playlist.Id,
                x.Playlist.Name,
                x.Playlist.Platform,
                x.Playlist.Url
            });

            return Ok(new { playlists });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving playlists for video {VideoId}", videoId);
            }

            return StatusCode(500, new { message = "An error occurred while retrieving playlists" });
        }
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
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Video with ID {VideoId} not found", videoId);
                }

                return NotFound(new { message = "Video not found" });
            }

            if (string.IsNullOrEmpty(video.FilePath) || !System.IO.File.Exists(video.FilePath))
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Video file not found for video ID {VideoId} at path {FilePath}", videoId, video.FilePath);
                }

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

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Streaming video {VideoId} from {FilePath}", videoId, video.FilePath);
            }

            // Enable range requests for seeking support
            return File(fileStream, contentType, enableRangeProcessing: true);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error streaming video {VideoId}", videoId);
            }

            return StatusCode(500, new { message = "An error occurred while streaming the video" });
        }
    }
}