namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for triggering file system scans
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = Constants.Roles.Administrator)]
public class FileSystemScanApiController : ControllerBase
{
    private readonly ILogger<FileSystemScanApiController> logger;
    private readonly FileSystemScanJob scanJob;

    public FileSystemScanApiController(
        ILogger<FileSystemScanApiController> logger,
        FileSystemScanJob scanJob)
    {
        this.logger = logger;
        this.scanJob = scanJob;
    }

    /// <summary>
    /// Scans the downloads folder for untracked video files
    /// </summary>
    [HttpPost("scan-filesystem")]
    public async Task<IActionResult> ScanFileSystem(CancellationToken cancellationToken)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("File system scan initiated by {User}", User.Identity?.Name);
            }

            var result = await scanJob.ExecuteAsync(cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error during file system scan");
            }

            return StatusCode(500, new { message = "An error occurred during the file system scan" });
        }
    }

    /// <summary>
    /// Retry fetching platform metadata for a video flagged as needing review
    /// </summary>
    [HttpPost("videos/{videoId:int}/retry-metadata")]
    public async Task<IActionResult> RetryMetadata(
        int videoId,
        [FromServices] IRepository<Video> videoRepository,
        [FromServices] VideoMetadataProviderFactory metadataProviderFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            var video = await videoRepository.FindOneAsync(new SearchOptions<Video>
            {
                CancellationToken = cancellationToken,
                Query = x => x.Id == videoId,
                Include = query => query.Include(x => x.Channel)
            });

            if (video is null)
            {
                return NotFound();
            }

            var provider = metadataProviderFactory.GetProviderByPlatform(video.Platform);
            if (provider is null)
            {
                return BadRequest(new { message = $"No metadata provider for platform '{video.Platform}'" });
            }

            var metadata = await provider.GetVideoMetadataAsync(video.VideoId, cancellationToken);
            if (metadata is null)
            {
                return Ok(new { success = false, message = "Metadata still unavailable from platform" });
            }

            video.Title = metadata.Title;
            video.Description = metadata.Description;
            video.ThumbnailUrl = metadata.ThumbnailUrl;
            video.Duration = metadata.Duration;
            video.UploadDate = metadata.UploadDate;
            video.ViewCount = metadata.ViewCount;
            video.LikeCount = metadata.LikeCount;
            video.Url = metadata.Url;
            video.NeedsMetadataReview = false;

            await videoRepository.UpdateAsync(video, ContextOptions.ForCancellationToken(cancellationToken));

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Successfully fetched metadata for video {VideoId}", videoId);
            }

            return Ok(new { success = true, message = "Metadata retrieved successfully" });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrying metadata for video {VideoId}", videoId);
            }

            return StatusCode(500, new { message = "An error occurred while retrying metadata" });
        }
    }
}