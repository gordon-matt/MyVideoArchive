namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for the user-facing video index (all accessible downloaded videos)
/// </summary>
[Authorize]
[ApiController]
[Route("api/video-index")]
public class VideoIndexApiController : ControllerBase
{
    private readonly ILogger<VideoIndexApiController> logger;
    private readonly IUserContextService userContextService;
    private readonly IRepository<Video> videoRepository;
    private readonly IRepository<Tag> tagRepository;
    private readonly IRepository<VideoTag> videoTagRepository;
    private readonly IRepository<UserChannel> userChannelRepository;
    private readonly IRepository<Channel> channelRepository;

    public VideoIndexApiController(
        ILogger<VideoIndexApiController> logger,
        IUserContextService userContextService,
        IRepository<Video> videoRepository,
        IRepository<Tag> tagRepository,
        IRepository<VideoTag> videoTagRepository,
        IRepository<UserChannel> userChannelRepository,
        IRepository<Channel> channelRepository)
    {
        this.logger = logger;
        this.userContextService = userContextService;
        this.videoRepository = videoRepository;
        this.tagRepository = tagRepository;
        this.videoTagRepository = videoTagRepository;
        this.userChannelRepository = userChannelRepository;
        this.channelRepository = channelRepository;
    }

    /// <summary>
    /// Get paginated videos accessible to the current user, with optional filters
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetVideos(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 60,
        [FromQuery] string? search = null,
        [FromQuery] int? channelId = null,
        [FromQuery] string? tagFilter = null)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            bool isAdmin = userContextService.IsAdministrator();

            // Build the base predicate
            var predicate = PredicateBuilder.New<Video>(false);

            if (isAdmin)
            {
                // Admins see all downloaded videos
                predicate = PredicateBuilder.New<Video>(x => x.DownloadedAt != null);
            }
            else
            {
                // Subscribed channel videos (downloaded)
                var subscribedChannelIds = (await userChannelRepository.FindAsync(
                    new SearchOptions<UserChannel> { Query = x => x.UserId == userId },
                    x => x.ChannelId)).ToList();

                if (subscribedChannelIds.Count > 0)
                {
                    predicate = predicate.Or(x =>
                        subscribedChannelIds.Contains(x.ChannelId) && x.DownloadedAt != null);
                }

                // Standalone videos (any download state)
                var standaloneTag = await tagRepository.FindOneAsync(new SearchOptions<Tag>
                {
                    Query = x => x.UserId == userId && x.Name == Constants.StandaloneTag
                });

                if (standaloneTag is not null)
                {
                    var standaloneVideoIds = (await videoTagRepository.FindAsync(
                        new SearchOptions<VideoTag> { Query = x => x.TagId == standaloneTag.Id },
                        x => x.VideoId)).ToList();

                    if (standaloneVideoIds.Count > 0)
                    {
                        predicate = predicate.Or(x => standaloneVideoIds.Contains(x.Id));
                    }
                }

                // If user has no subscriptions and no standalone videos, return empty
                if (!predicate.IsStarted)
                {
                    return Ok(new
                    {
                        videos = Array.Empty<object>(),
                        pagination = new { currentPage = page, pageSize, totalCount = 0, totalPages = 0 }
                    });
                }
            }

            // Optional: filter by channel
            if (channelId.HasValue)
            {
                predicate = predicate.And(x => x.ChannelId == channelId.Value);
            }

            // Optional: filter by title search
            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchLower = search.Trim().ToLower();
                predicate = predicate.And(x => x.Title.ToLower().Contains(searchLower));
            }

            // Optional: filter by tag names
            if (!string.IsNullOrWhiteSpace(tagFilter))
            {
                var tagNames = tagFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (tagNames.Length > 0)
                {
                    var matchingTagIds = (await tagRepository.FindAsync(
                        new SearchOptions<Tag>
                        {
                            Query = x => x.UserId == userId && tagNames.Contains(x.Name)
                        },
                        x => x.Id)).ToList();

                    if (matchingTagIds.Count > 0)
                    {
                        predicate = predicate.And(x => x.VideoTags.Any(vt => matchingTagIds.Contains(vt.TagId)));
                    }
                    else
                    {
                        // No matching tags → no results
                        return Ok(new
                        {
                            videos = Array.Empty<object>(),
                            pagination = new { currentPage = page, pageSize, totalCount = 0, totalPages = 0 }
                        });
                    }
                }
            }

            var options = new SearchOptions<Video>
            {
                CancellationToken = HttpContext.RequestAborted,
                Query = predicate,
                OrderBy = q => q.OrderByDescending(x => x.DownloadedAt ?? x.UploadDate),
                PageNumber = page,
                PageSize = pageSize,
                Include = q => q.Include(x => x.Channel)
                               .Include(x => x.VideoTags)
                               .ThenInclude(vt => vt.Tag)
            };

            var pagedVideos = await videoRepository.FindAsync(options);

            var videos = pagedVideos.Select(x => new
            {
                x.Id,
                x.VideoId,
                x.Title,
                x.ThumbnailUrl,
                Duration = x.Duration,
                x.UploadDate,
                x.DownloadedAt,
                x.IsQueued,
                x.Platform,
                Channel = new { x.Channel.Id, x.Channel.Name },
                Tags = x.VideoTags
                    .Where(vt => vt.Tag.UserId == userId)
                    .Select(vt => vt.Tag.Name)
                    .ToList()
            }).ToList();

            return Ok(new
            {
                videos,
                pagination = new
                {
                    currentPage = page,
                    pageSize,
                    totalCount = pagedVideos.ItemCount,
                    totalPages = pagedVideos.PageCount
                }
            });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(ex, "Error retrieving video index");

            return StatusCode(500, new { message = "An error occurred while retrieving videos" });
        }
    }

    /// <summary>
    /// Get channels accessible to the current user (for the channel filter dropdown)
    /// </summary>
    [HttpGet("channels")]
    public async Task<IActionResult> GetAccessibleChannels()
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            bool isAdmin = userContextService.IsAdministrator();

            if (isAdmin)
            {
                var allChannels = await channelRepository.FindAsync(
                    new SearchOptions<Channel> { OrderBy = q => q.OrderBy(x => x.Name) },
                    x => new { x.Id, x.Name });
                return Ok(new { channels = allChannels });
            }

            var subscribedChannelIds = (await userChannelRepository.FindAsync(
                new SearchOptions<UserChannel> { Query = x => x.UserId == userId },
                x => x.ChannelId)).ToList();

            // Also include channels of standalone videos
            var standaloneTag = await tagRepository.FindOneAsync(new SearchOptions<Tag>
            {
                Query = x => x.UserId == userId && x.Name == Constants.StandaloneTag
            });

            if (standaloneTag is not null)
            {
                var standaloneVideoChannelIds = await videoTagRepository.FindAsync(
                    new SearchOptions<VideoTag>
                    {
                        Query = x => x.TagId == standaloneTag.Id,
                        Include = q => q.Include(x => x.Video)
                    },
                    x => x.Video.ChannelId);

                subscribedChannelIds = subscribedChannelIds
                    .Union(standaloneVideoChannelIds)
                    .Distinct()
                    .ToList();
            }

            if (subscribedChannelIds.Count == 0)
                return Ok(new { channels = Array.Empty<object>() });

            var channels = await channelRepository.FindAsync(
                new SearchOptions<Channel>
                {
                    Query = x => subscribedChannelIds.Contains(x.Id),
                    OrderBy = q => q.OrderBy(x => x.Name)
                },
                x => new { x.Id, x.Name });

            return Ok(new { channels });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(ex, "Error retrieving accessible channels");

            return StatusCode(500, new { message = "An error occurred while retrieving channels" });
        }
    }
}