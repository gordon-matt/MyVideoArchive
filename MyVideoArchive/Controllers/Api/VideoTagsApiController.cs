namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for managing video tags
/// </summary>
[Authorize]
[ApiController]
[Route("api/videos/{videoId}/tags")]
public class VideoTagsApiController : ControllerBase
{
    private readonly ILogger<VideoTagsApiController> logger;
    private readonly IUserContextService userContextService;
    private readonly IRepository<Tag> tagRepository;
    private readonly IRepository<UserChannel> userChannelRepository;
    private readonly IRepository<Video> videoRepository;
    private readonly IRepository<VideoTag> videoTagRepository;

    public VideoTagsApiController(
        ILogger<VideoTagsApiController> logger,
        IUserContextService userContextService,
        IRepository<Tag> tagRepository,
        IRepository<UserChannel> userChannelRepository,
        IRepository<Video> videoRepository,
        IRepository<VideoTag> videoTagRepository)
    {
        this.logger = logger;
        this.userContextService = userContextService;
        this.tagRepository = tagRepository;
        this.userChannelRepository = userChannelRepository;
        this.videoRepository = videoRepository;
        this.videoTagRepository = videoTagRepository;
    }

    /// <summary>
    /// Get all tags applied to a video for the current user
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetVideoTags(int videoId)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            bool videoExists = await videoRepository.ExistsAsync(x => x.Id == videoId);
            if (!videoExists)
            {
                return NotFound();
            }

            var videoTags = await videoTagRepository.FindAsync(new SearchOptions<VideoTag>
            {
                Query = x => x.VideoId == videoId && x.Tag.UserId == userId,
                Include = q => q.Include(x => x.Tag)
            });

            var tags = videoTags.Select(vt => new { id = vt.Tag.Id, name = vt.Tag.Name }).ToList();

            return Ok(new { tags });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving tags for video {VideoId}", videoId);
            }

            return StatusCode(500, new { message = "An error occurred while retrieving tags" });
        }
    }

    /// <summary>
    /// Set the tags for a video (replaces existing tags for this user)
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> SetVideoTags(int videoId, [FromBody] SetVideoTagsRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var video = await videoRepository.FindOneAsync(videoId);

            if (video is null)
            {
                return NotFound();
            }

            var standaloneTag = await GetOrCreateTagAsync(userId, Constants.StandaloneTag);

            // Remove all existing tags for this user on this video
            await videoTagRepository.DeleteAsync(x =>
                x.TagId != standaloneTag.Id &&
                x.VideoId == videoId &&
                x.Tag.UserId == userId);

            if (!request.TagNames.IsNullOrEmpty())
            {
                foreach (string? tagName in request.TagNames.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    string trimmed = tagName.Trim();
                    if (string.IsNullOrEmpty(trimmed))
                    {
                        continue;
                    }

                    var tag = await GetOrCreateTagAsync(userId, trimmed);
                    await videoTagRepository.InsertAsync(new VideoTag
                    {
                        VideoId = videoId,
                        TagId = tag.Id
                    });
                }
            }

            return Ok(new { message = "Tags updated" });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error setting tags for video {VideoId}", videoId);
            }

            return StatusCode(500, new { message = "An error occurred while updating tags" });
        }
    }

    private async Task<Tag> GetOrCreateTagAsync(string userId, string name)
    {
        var existing = await tagRepository.FindOneAsync(new SearchOptions<Tag>
        {
            Query = x => x.UserId == userId && x.Name.ToLower() == name.ToLower()
        });

        if (existing is not null)
        {
            return existing;
        }

        var tag = new Tag { UserId = userId, Name = name };
        await tagRepository.InsertAsync(tag);
        return tag;
    }
}

public class SetVideoTagsRequest
{
    public List<string> TagNames { get; set; } = [];
}