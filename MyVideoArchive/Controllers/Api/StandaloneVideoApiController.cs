using Hangfire;
using MyVideoArchive.Models.Metadata;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for adding standalone videos and retrieving standalone video info
/// </summary>
[Authorize]
[ApiController]
[Route("api/videos")]
public class StandaloneVideoApiController : ControllerBase
{
    private readonly ILogger<StandaloneVideoApiController> logger;
    private readonly IUserContextService userContextService;
    private readonly IBackgroundJobClient backgroundJobClient;
    private readonly VideoMetadataProviderFactory metadataProviderFactory;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<Video> videoRepository;
    private readonly IRepository<Tag> tagRepository;
    private readonly IRepository<VideoTag> videoTagRepository;
    private readonly IRepository<UserChannel> userChannelRepository;

    public StandaloneVideoApiController(
        ILogger<StandaloneVideoApiController> logger,
        IUserContextService userContextService,
        IBackgroundJobClient backgroundJobClient,
        VideoMetadataProviderFactory metadataProviderFactory,
        IRepository<Channel> channelRepository,
        IRepository<Video> videoRepository,
        IRepository<Tag> tagRepository,
        IRepository<VideoTag> videoTagRepository,
        IRepository<UserChannel> userChannelRepository)
    {
        this.logger = logger;
        this.userContextService = userContextService;
        this.backgroundJobClient = backgroundJobClient;
        this.metadataProviderFactory = metadataProviderFactory;
        this.channelRepository = channelRepository;
        this.videoRepository = videoRepository;
        this.tagRepository = tagRepository;
        this.videoTagRepository = videoTagRepository;
        this.userChannelRepository = userChannelRepository;
    }

    /// <summary>
    /// Add a standalone video by URL. Fetches metadata, creates channel if needed,
    /// tags as standalone, and queues for download.
    /// </summary>
    [HttpPost("standalone")]
    public async Task<IActionResult> AddStandaloneVideo([FromBody] AddStandaloneVideoRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Url))
                return BadRequest(new { message = "A video URL is required" });

            var provider = metadataProviderFactory.GetProvider(request.Url);
            if (provider is null)
                return BadRequest(new { message = "No metadata provider found for this URL" });

            // Fetch video metadata
            var videoMeta = await provider.GetVideoMetadataAsync(request.Url, HttpContext.RequestAborted);
            if (videoMeta is null)
                return BadRequest(new { message = "Could not retrieve video metadata. Please check the URL and try again." });

            // Find or create the channel
            var channel = await channelRepository.FindOneAsync(new SearchOptions<Channel>
            {
                Query = x => x.ChannelId == videoMeta.ChannelId && x.Platform == videoMeta.Platform
            });

            if (channel is null)
            {
                // Fetch channel metadata to create a proper channel record
                ChannelMetadata? channelMeta = null;
                if (!string.IsNullOrEmpty(videoMeta.ChannelId))
                {
                    var channelUrl = $"https://www.youtube.com/channel/{videoMeta.ChannelId}";
                    channelMeta = await provider.GetChannelMetadataAsync(channelUrl, HttpContext.RequestAborted);
                }

                channel = new Channel
                {
                    ChannelId = videoMeta.ChannelId ?? videoMeta.ChannelName ?? "unknown",
                    Name = channelMeta?.Name ?? videoMeta.ChannelName ?? "Unknown Channel",
                    Url = channelMeta?.Url ?? (string.IsNullOrEmpty(videoMeta.ChannelId)
                        ? string.Empty
                        : $"https://www.youtube.com/channel/{videoMeta.ChannelId}"),
                    Description = channelMeta?.Description,
                    ThumbnailUrl = channelMeta?.ThumbnailUrl,
                    SubscriberCount = channelMeta?.SubscriberCount,
                    Platform = videoMeta.Platform,
                    SubscribedAt = DateTime.UtcNow
                };

                await channelRepository.InsertAsync(channel);

                if (logger.IsEnabled(LogLevel.Information))
                    logger.LogInformation("Created channel {ChannelId} for standalone video", channel.ChannelId);
            }

            // Check if the video already exists
            var video = await videoRepository.FindOneAsync(new SearchOptions<Video>
            {
                Query = x => x.VideoId == videoMeta.VideoId && x.Platform == videoMeta.Platform
            });

            if (video is null)
            {
                video = new Video
                {
                    VideoId = videoMeta.VideoId,
                    Title = videoMeta.Title,
                    Description = videoMeta.Description,
                    Url = videoMeta.Url,
                    ThumbnailUrl = videoMeta.ThumbnailUrl,
                    Platform = videoMeta.Platform,
                    Duration = videoMeta.Duration,
                    UploadDate = videoMeta.UploadDate,
                    ViewCount = videoMeta.ViewCount,
                    LikeCount = videoMeta.LikeCount,
                    ChannelId = channel.Id,
                    IsQueued = true
                };

                await videoRepository.InsertAsync(video);

                if (logger.IsEnabled(LogLevel.Information))
                    logger.LogInformation("Created standalone video {VideoId}", video.VideoId);
            }

            // Get or create "standalone" tag for this user
            var standaloneTag = await GetOrCreateTagAsync(userId, Constants.StandaloneTag);

            // Tag the video as standalone if not already tagged
            var alreadyTagged = await videoTagRepository.ExistsAsync(x => x.VideoId == video.Id && x.TagId == standaloneTag.Id);
            if (!alreadyTagged)
            {
                await videoTagRepository.InsertAsync(new VideoTag
                {
                    VideoId = video.Id,
                    TagId = standaloneTag.Id
                });
            }

            // Queue download if not already downloaded
            if (video.DownloadedAt is null)
            {
                video.IsQueued = true;
                await videoRepository.UpdateAsync(video);
                backgroundJobClient.Enqueue<VideoDownloadJob>(job => job.ExecuteAsync(video.Id, CancellationToken.None));
            }

            return Ok(new
            {
                videoId = video.Id,
                title = video.Title,
                channelId = channel.Id,
                channelName = channel.Name,
                isAlreadyDownloaded = video.DownloadedAt is not null
            });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(ex, "Error adding standalone video from URL {Url}", request.Url);

            return StatusCode(500, new { message = "An error occurred while adding the video" });
        }
    }

    /// <summary>
    /// Get standalone status info for a video (for the banner on the details page)
    /// </summary>
    [HttpGet("{videoId}/standalone-info")]
    public async Task<IActionResult> GetStandaloneInfo(int videoId)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var video = await videoRepository.FindOneAsync(new SearchOptions<Video>
            {
                Query = x => x.Id == videoId,
                Include = q => q.Include(x => x.Channel)
            });

            if (video is null) return NotFound();

            // Check if user has the "standalone" tag on this video
            var standaloneTag = await tagRepository.FindOneAsync(new SearchOptions<Tag>
            {
                Query = x => x.UserId == userId && x.Name == Constants.StandaloneTag
            });

            bool isStandalone = false;
            if (standaloneTag is not null)
            {
                isStandalone = await videoTagRepository.ExistsAsync(x => x.VideoId == videoId && x.TagId == standaloneTag.Id);
            }

            // Count archived (downloaded) videos from this channel
            var channelVideos = await videoRepository.FindAsync(
                new SearchOptions<Video>
                {
                    Query = x => x.ChannelId == video.ChannelId && x.DownloadedAt != null
                },
                x => x.Id);

            int channelVideoCount = channelVideos.ItemCount;

            // Check if user is already subscribed to the channel
            bool isSubscribed = await userChannelRepository.ExistsAsync(
                x => x.UserId == userId && x.ChannelId == video.ChannelId);

            return Ok(new
            {
                isStandalone,
                channelVideoCount,
                channelId = video.ChannelId,
                channelName = video.Channel.Name,
                channelUrl = video.Channel.Url,
                channelPlatformId = video.Channel.ChannelId,
                channelPlatform = video.Channel.Platform,
                isSubscribed
            });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(ex, "Error retrieving standalone info for video {VideoId}", videoId);

            return StatusCode(500, new { message = "An error occurred while retrieving video info" });
        }
    }

    private async Task<Tag> GetOrCreateTagAsync(string userId, string name)
    {
        var existing = await tagRepository.FindOneAsync(new SearchOptions<Tag>
        {
            Query = x => x.UserId == userId && x.Name.ToLower() == name.ToLower()
        });

        if (existing is not null) return existing;

        var tag = new Tag { UserId = userId, Name = name };
        await tagRepository.InsertAsync(tag);
        return tag;
    }
}

public class AddStandaloneVideoRequest
{
    public string Url { get; set; } = string.Empty;
}