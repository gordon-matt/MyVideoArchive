using Hangfire;
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
    private readonly ILogger<ChannelVideosApiController> logger;
    private readonly IUserContextService userContextService;
    private readonly IBackgroundJobClient backgroundJobClient;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<UserChannel> userChannelRepository;
    private readonly IRepository<Video> videoRepository;

    public ChannelVideosApiController(
        ILogger<ChannelVideosApiController> logger,
        IUserContextService userContextService,
        IBackgroundJobClient backgroundJobClient,
        IRepository<Channel> channelRepository,
        IRepository<UserChannel> userChannelRepository,
        IRepository<Video> videoRepository)
    {
        this.videoRepository = videoRepository;
        this.channelRepository = channelRepository;
        this.userChannelRepository = userChannelRepository;
        this.backgroundJobClient = backgroundJobClient;
        this.userContextService = userContextService;
        this.logger = logger;
    }

    private async Task<bool> UserHasAccessToChannel(int channelId)
    {
        if (userContextService.IsAdministrator())
        {
            return true;
        }

        string? userId = userContextService.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return false;
        }

        var userChannel = await userChannelRepository.FindOneAsync(new SearchOptions<UserChannel>
        {
            Query = x =>
                x.UserId == userId &&
                x.ChannelId == channelId
        });

        return userChannel is not null;
    }

    /// <summary>
    /// Get downloaded videos for a channel with optional search and playlist filtering (paginated)
    /// </summary>
    [HttpGet("downloaded")]
    public async Task<IActionResult> GetDownloadedVideos(
        int channelId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 24,
        [FromQuery] string? search = null,
        [FromQuery] int? playlistId = null)
    {
        try
        {
            if (!await UserHasAccessToChannel(channelId))
            {
                return Forbid();
            }

            var channelExists = await channelRepository.ExistsAsync(x => x.Id == channelId);
            if (!channelExists)
            {
                return NotFound(new { message = "Channel not found" });
            }

            var predicate = PredicateBuilder.New<Video>(x => x.ChannelId == channelId && x.DownloadedAt != null);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchLower = search.Trim().ToLower();
                predicate = predicate.And(x => x.Title.ToLower().Contains(searchLower));
            }

            if (playlistId.HasValue)
            {
                if (playlistId.Value == -1)
                {
                    // Videos not in any playlist
                    predicate = predicate.And(x => !x.PlaylistVideos.Any());
                }
                else if (playlistId.Value > 0)
                {
                    predicate = predicate.And(x => x.PlaylistVideos.Any(pv => pv.PlaylistId == playlistId.Value));
                }
            }

            var options = new SearchOptions<Video>
            {
                CancellationToken = HttpContext.RequestAborted,
                Query = predicate,
                OrderBy = query => query.OrderByDescending(x => x.UploadDate),
                PageNumber = page,
                PageSize = pageSize
            };

            var videos = await videoRepository.FindAsync(options, x => new
            {
                x.Id,
                x.VideoId,
                x.Title,
                x.Url,
                x.ThumbnailUrl,
                x.Duration,
                x.UploadDate,
                x.DownloadedAt
            });

            return Ok(new
            {
                videos,
                pagination = new
                {
                    currentPage = page,
                    pageSize,
                    totalCount = videos.ItemCount,
                    totalPages = videos.PageCount
                }
            });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving downloaded videos for channel {ChannelId}", channelId);
            }

            return StatusCode(500, new { message = "An error occurred while retrieving videos" });
        }
    }

    /// <summary>
    /// Get available videos for a channel (paginated)
    /// </summary>
    [HttpGet("available")]
    public async Task<IActionResult> GetAvailableVideos(
        int channelId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool showIgnored = false)
    {
        try
        {
            // Check user access
            if (!await UserHasAccessToChannel(channelId))
            {
                return Forbid();
            }

            var channel = await channelRepository.FindOneAsync(channelId);
            if (channel is null)
            {
                return NotFound(new { message = "Channel not found" });
            }

            var predicate = PredicateBuilder.New<Video>(x => x.ChannelId == channelId);

            // Only show videos that haven't been downloaded yet and aren't queued
            predicate = predicate.And(x => x.DownloadedAt == null && !x.IsQueued);

            // Exclude private videos
            predicate = predicate.And(x => x.Title != Constants.PrivateVideoTitle);

            // Filter based on showIgnored flag
            if (!showIgnored)
            {
                predicate = predicate.And(x => !x.IsIgnored);
            }

            var options = new SearchOptions<Video>
            {
                CancellationToken = HttpContext.RequestAborted,
                Query = predicate,
                OrderBy = query => query.OrderByDescending(x => x.UploadDate),
                PageNumber = page,
                PageSize = pageSize
            };

            // Apply pagination
            var videos = await videoRepository.FindAsync(options, x => new
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
            });

            return Ok(new
            {
                videos,
                pagination = new
                {
                    currentPage = page,
                    pageSize,
                    totalCount = videos.ItemCount,
                    totalPages = videos.PageCount
                }
            });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving available videos for channel {ChannelId}", channelId);
            }

            return StatusCode(500, new { message = "An error occurred while retrieving videos" });
        }
    }

    /// <summary>
    /// Download selected videos
    /// </summary>
    [HttpPost("download")]
    public async Task<IActionResult> DownloadVideos(int channelId, [FromBody] DownloadVideosRequest request)
    {
        try
        {
            // Check user access
            if (!await UserHasAccessToChannel(channelId))
            {
                return Forbid();
            }

            if (request.VideoIds.IsNullOrEmpty())
            {
                return BadRequest(new { message = "No video IDs provided" });
            }

            var videos = await videoRepository.FindAsync(new SearchOptions<Video>
            {
                Query = x =>
                    x.ChannelId == channelId &&
                    request.VideoIds.Contains(x.Id)
            });

            if (videos.Count == 0)
            {
                return NotFound(new { message = "No videos found" });
            }

            var videoUpdates = new List<Video>();
            int queuedCount = 0;
            foreach (var video in videos)
            {
                // Only queue if not already downloaded and not already queued
                if (video.DownloadedAt is null && !video.IsQueued)
                {
                    video.IsQueued = true;
                    videoUpdates.Add(video);

                    backgroundJobClient.Enqueue<VideoDownloadJob>(job =>
                        job.ExecuteAsync(video.Id, CancellationToken.None));
                    queuedCount++;
                }
            }

            await videoRepository.UpdateAsync(videoUpdates);

            return Ok(new
            {
                message = $"Queued {queuedCount} video(s) for download",
                queuedCount
            });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error queueing video downloads for channel {ChannelId}", channelId);
            }

            return StatusCode(500, new { message = "An error occurred while queueing downloads" });
        }
    }

    /// <summary>
    /// Download all available videos for a channel
    /// </summary>
    [HttpPost("download-all")]
    public async Task<IActionResult> DownloadAllVideos(int channelId)
    {
        try
        {
            // Check user access
            if (!await UserHasAccessToChannel(channelId))
            {
                return Forbid();
            }

            var videos = await videoRepository.FindAsync(new SearchOptions<Video>
            {
                Query = x =>
                    x.ChannelId == channelId &&
                    x.DownloadedAt == null &&
                    !x.IsIgnored &&
                    !x.IsQueued
            });

            if (videos.Count == 0)
            {
                return Ok(new { message = "No videos available to download", queuedCount = 0 });
            }

            var videoUpdates = new List<Video>();
            foreach (var video in videos)
            {
                video.IsQueued = true;
                videoUpdates.Add(video);

                backgroundJobClient.Enqueue<VideoDownloadJob>(job =>
                    job.ExecuteAsync(video.Id, CancellationToken.None));
            }

            await videoRepository.UpdateAsync(videoUpdates);

            return Ok(new
            {
                message = $"Queued {videos.Count} video(s) for download",
                queuedCount = videos.Count
            });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error queueing all video downloads for channel {ChannelId}", channelId);
            }

            return StatusCode(500, new { message = "An error occurred while queueing downloads" });
        }
    }

    /// <summary>
    /// Delete the physical file for a downloaded video, clearing download metadata and marking it ignored
    /// </summary>
    [HttpDelete("{videoId}/file")]
    public async Task<IActionResult> DeleteVideoFile(int channelId, int videoId)
    {
        try
        {
            if (!await UserHasAccessToChannel(channelId))
            {
                return Forbid();
            }

            var video = await videoRepository.FindOneAsync(new SearchOptions<Video>
            {
                Query = x => x.Id == videoId && x.ChannelId == channelId
            });

            if (video is null)
            {
                return NotFound(new { message = "Video not found" });
            }

            if (!string.IsNullOrEmpty(video.FilePath) && System.IO.File.Exists(video.FilePath))
            {
                System.IO.File.Delete(video.FilePath);
            }

            video.DownloadedAt = null;
            video.FilePath = null;
            video.FileSize = null;
            video.IsIgnored = true;
            await videoRepository.UpdateAsync(video);

            return Ok(new { message = "Video file deleted successfully" });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error deleting video file for video {VideoId}", videoId);
            }

            return StatusCode(500, new { message = "An error occurred while deleting the video file" });
        }
    }

    /// <summary>
    /// Toggle ignore status for a video
    /// </summary>
    [HttpPut("{videoId}/ignore")]
    public async Task<IActionResult> ToggleIgnore(int channelId, int videoId, [FromBody] IgnoreVideoRequest request)
    {
        try
        {
            // Check user access
            if (!await UserHasAccessToChannel(channelId))
            {
                return Forbid();
            }

            var video = await videoRepository.FindOneAsync(new SearchOptions<Video>
            {
                Query = x =>
                    x.Id == videoId &&
                    x.ChannelId == channelId
            });

            if (video is null)
            {
                return NotFound(new { message = "Video not found" });
            }

            video.IsIgnored = request.IsIgnored;
            await videoRepository.UpdateAsync(video);

            return Ok(new
            {
                message = request.IsIgnored ? "Video ignored" : "Video unignored",
                isIgnored = video.IsIgnored
            });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error toggling ignore status for video {VideoId}", videoId);
            }

            return StatusCode(500, new { message = "An error occurred while updating video status" });
        }
    }
}