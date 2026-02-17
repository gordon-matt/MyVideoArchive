using Extenso.Data.Entity;
using Hangfire;
using LinqKit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyVideoArchive.Data.Entities;
using MyVideoArchive.Services;
using MyVideoArchive.Services.Jobs;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for managing channel videos (available, download, ignore operations)
/// </summary>
[Authorize]
[ApiController]
[Route("api/channels/{channelId}/videos")]
public class ChannelVideosApiController : ControllerBase
{
    private readonly IRepository<Video> _videoRepository;
    private readonly IRepository<Channel> _channelRepository;
    private readonly IRepository<UserChannel> _userChannelRepository;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IUserContextService _userContext;
    private readonly ILogger<ChannelVideosApiController> _logger;

    public ChannelVideosApiController(
        IRepository<Video> videoRepository,
        IRepository<Channel> channelRepository,
        IRepository<UserChannel> userChannelRepository,
        IBackgroundJobClient backgroundJobClient,
        IUserContextService userContext,
        ILogger<ChannelVideosApiController> logger)
    {
        _videoRepository = videoRepository;
        _channelRepository = channelRepository;
        _userChannelRepository = userChannelRepository;
        _backgroundJobClient = backgroundJobClient;
        _userContext = userContext;
        _logger = logger;
    }
    
    private async Task<bool> UserHasAccessToChannel(int channelId)
    {
        if (_userContext.IsAdministrator())
        {
            return true;
        }
        
        var userId = _userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return false;
        }
        
        var userChannel = await _userChannelRepository.FindOneAsync(new SearchOptions<UserChannel>
        {
            Query = uc => uc.UserId == userId && uc.ChannelId == channelId
        });
        
        return userChannel != null;
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
            
            var channel = await _channelRepository.FindOneAsync(channelId);
            if (channel == null)
            {
                return NotFound(new { message = "Channel not found" });
            }

            var predicate = PredicateBuilder.New<Video>(v => v.ChannelId == channelId);

            // Only show videos that haven't been downloaded yet and aren't queued
            predicate = predicate.And(v => v.DownloadedAt == null && !v.IsQueued);

            // Filter based on showIgnored flag
            if (!showIgnored)
            {
                predicate = predicate.And(v => !v.IsIgnored);
            }

            var options = new SearchOptions<Video>
            {
                CancellationToken = HttpContext.RequestAborted,
                Query = predicate,
                OrderBy = query => query.OrderByDescending(v => v.UploadDate),
                PageNumber = page,
                PageSize = pageSize
            };

            // Get total count for pagination
            var totalCount = await _videoRepository.CountAsync(options);

            // Apply pagination
            var videos = await _videoRepository.FindAsync(options, v => new
            {
                v.Id,
                v.VideoId,
                v.Title,
                v.Description,
                v.Url,
                v.ThumbnailUrl,
                v.Duration,
                v.UploadDate,
                v.ViewCount,
                v.LikeCount,
                v.DownloadedAt,
                v.IsIgnored,
                IsDownloaded = v.DownloadedAt != null
            });

            return Ok(new
            {
                videos,
                pagination = new
                {
                    currentPage = page,
                    pageSize,
                    totalCount,
                    totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving available videos for channel {ChannelId}", channelId);
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
            
            if (request.VideoIds == null || request.VideoIds.Count == 0)
            {
                return BadRequest(new { message = "No video IDs provided" });
            }

            var videos = await _videoRepository.FindAsync(new SearchOptions<Video>
            {
                Query = v => v.ChannelId == channelId && request.VideoIds.Contains(v.Id)
            });

            if (videos.Count == 0)
            {
                return NotFound(new { message = "No videos found" });
            }

            var queuedCount = 0;
            foreach (var video in videos)
            {
                // Only queue if not already downloaded and not already queued
                if (video.DownloadedAt == null && !video.IsQueued)
                {
                    video.IsQueued = true;
                    await _videoRepository.UpdateAsync(video);
                    
                    _backgroundJobClient.Enqueue<VideoDownloadJob>(job =>
                        job.ExecuteAsync(video.Id, CancellationToken.None));
                    queuedCount++;
                }
            }

            return Ok(new
            {
                message = $"Queued {queuedCount} video(s) for download",
                queuedCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error queueing video downloads for channel {ChannelId}", channelId);
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
            
            var videos = await _videoRepository.FindAsync(new SearchOptions<Video>
            {
                Query = v => v.ChannelId == channelId 
                    && v.DownloadedAt == null 
                    && !v.IsIgnored
                    && !v.IsQueued
            });

            if (videos.Count == 0)
            {
                return Ok(new { message = "No videos available to download", queuedCount = 0 });
            }

            foreach (var video in videos)
            {
                video.IsQueued = true;
                await _videoRepository.UpdateAsync(video);
                
                _backgroundJobClient.Enqueue<VideoDownloadJob>(job =>
                    job.ExecuteAsync(video.Id, CancellationToken.None));
            }

            return Ok(new
            {
                message = $"Queued {videos.Count} video(s) for download",
                queuedCount = videos.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error queueing all video downloads for channel {ChannelId}", channelId);
            return StatusCode(500, new { message = "An error occurred while queueing downloads" });
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
            
            var video = await _videoRepository.FindOneAsync(new SearchOptions<Video>
            {
                Query = v => v.Id == videoId && v.ChannelId == channelId
            });

            if (video == null)
            {
                return NotFound(new { message = "Video not found" });
            }

            video.IsIgnored = request.IsIgnored;
            await _videoRepository.UpdateAsync(video);

            return Ok(new
            {
                message = request.IsIgnored ? "Video ignored" : "Video unignored",
                isIgnored = video.IsIgnored
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling ignore status for video {VideoId}", videoId);
            return StatusCode(500, new { message = "An error occurred while updating video status" });
        }
    }
}

public class DownloadVideosRequest
{
    public List<int> VideoIds { get; set; } = [];
}

public class IgnoreVideoRequest
{
    public bool IsIgnored { get; set; }
}