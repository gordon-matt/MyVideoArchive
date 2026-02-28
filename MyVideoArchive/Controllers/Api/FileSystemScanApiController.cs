using MyVideoArchive.Models;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for file system scan operations.
/// Scans run in background; poll /status for progress and call /cancel to stop early.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = Constants.Roles.Administrator)]
public class FileSystemScanApiController : ControllerBase
{
    private readonly ILogger<FileSystemScanApiController> logger;
    private readonly FileSystemScanStateService scanState;
    private readonly IServiceScopeFactory scopeFactory;

    public FileSystemScanApiController(
        ILogger<FileSystemScanApiController> logger,
        FileSystemScanStateService scanState,
        IServiceScopeFactory scopeFactory)
    {
        this.logger = logger;
        this.scanState = scanState;
        this.scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Starts a background file system scan across all channels.
    /// Returns 202 Accepted immediately; poll /status for progress.
    /// Returns 409 Conflict if a scan is already running.
    /// </summary>
    [HttpPost("scan-filesystem")]
    public IActionResult ScanFileSystem()
    {
        if (!scanState.TryStart(out var cancellationToken))
        {
            return Conflict(new { message = "A file system scan is already in progress." });
        }

        string? userName = User.Identity?.Name;

        _ = Task.Run(async () =>
        {
            logger.LogInformation("File system scan (all channels) initiated by {User}", userName);

            try
            {
                using var scope = scopeFactory.CreateScope();
                var scanJob = scope.ServiceProvider.GetRequiredService<FileSystemScanJob>();
                var progress = new Progress<FileSystemScanProgress>(p => scanState.UpdateProgress(p));
                var result = await scanJob.ExecuteAsync(null, progress, cancellationToken);
                scanState.Complete(result);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("File system scan was cancelled");
                scanState.Complete(new FileSystemScanResult());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during file system scan");
                scanState.Fail("An error occurred during the scan. Check the server logs for details.");
            }
        });

        return Accepted();
    }

    /// <summary>
    /// Starts a background file system scan for a single channel.
    /// Returns 202 Accepted immediately; poll /status for progress.
    /// Returns 409 Conflict if a scan is already running.
    /// </summary>
    [HttpPost("channels/{channelId:int}/scan-filesystem")]
    public IActionResult ScanFileSystemByChannel(int channelId)
    {
        if (!scanState.TryStart(out var cancellationToken))
        {
            return Conflict(new { message = "A file system scan is already in progress." });
        }

        string? userName = User.Identity?.Name;

        _ = Task.Run(async () =>
        {
            logger.LogInformation("File system scan for channel {ChannelId} initiated by {User}", channelId, userName);

            try
            {
                using var scope = scopeFactory.CreateScope();
                var scanJob = scope.ServiceProvider.GetRequiredService<FileSystemScanJob>();
                var progress = new Progress<FileSystemScanProgress>(p => scanState.UpdateProgress(p));
                var result = await scanJob.ExecuteAsync(channelId, progress, cancellationToken);
                scanState.Complete(result);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("File system scan for channel {ChannelId} was cancelled", channelId);
                scanState.Complete(new FileSystemScanResult());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during file system scan for channel {ChannelId}", channelId);
                scanState.Fail("An error occurred during the scan. Check the server logs for details.");
            }
        });

        return Accepted();
    }

    /// <summary>
    /// Returns the current status of any running or recently completed file system scan.
    /// </summary>
    [HttpGet("scan-filesystem/status")]
    public IActionResult GetScanStatus() => Ok(scanState.GetStatus());

    /// <summary>
    /// Requests cancellation of the currently running file system scan.
    /// </summary>
    [HttpPost("scan-filesystem/cancel")]
    public IActionResult CancelScan()
    {
        scanState.Cancel();
        return Ok(new { message = "Cancellation requested." });
    }

    /// <summary>
    /// Retry fetching platform metadata for a video flagged as needing review.
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
            if (metadata is null || metadata.Title == Constants.PrivateVideoTitle)
            {
                return Ok(new { success = false, message = "Metadata still unavailable from platform" });
            }

            if (metadata.Title == Constants.DeletedVideoTitle)
            {
                // TODO: We need a way to mark videos as deleted, so we can display in UI
                video.NeedsMetadataReview = false;
                await videoRepository.UpdateAsync(video, ContextOptions.ForCancellationToken(cancellationToken));
                return Ok(new { success = false, message = "Video was deleted from platform" });
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

            logger.LogInformation("Successfully fetched metadata for video {VideoId}", videoId);

            return Ok(new { success = true, message = "Metadata retrieved successfully" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrying metadata for video {VideoId}", videoId);
            return StatusCode(500, new { message = "An error occurred while retrying metadata" });
        }
    }
}