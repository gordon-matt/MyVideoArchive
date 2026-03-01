namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for retrieving user-level tag lists (for Tagify autocomplete)
/// </summary>
[Authorize]
[ApiController]
[Route("api/tags")]
public class UserTagsApiController : ControllerBase
{
    private readonly ILogger<UserTagsApiController> logger;
    private readonly IUserContextService userContextService;
    private readonly IRepository<Tag> tagRepository;

    public UserTagsApiController(
        ILogger<UserTagsApiController> logger,
        IUserContextService userContextService,
        IRepository<Tag> tagRepository)
    {
        this.logger = logger;
        this.userContextService = userContextService;
        this.tagRepository = tagRepository;
    }

    /// <summary>
    /// Get all tag names for the current user (for autocomplete)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetUserTags()
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var tags = await tagRepository.FindAsync(
                new SearchOptions<Tag>
                {
                    Query = x => x.UserId == userId,
                    OrderBy = q => q.OrderBy(x => x.Name)
                },
                x => new { x.Id, x.Name });

            return Ok(new { tags });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(ex, "Error retrieving tags for user");

            return StatusCode(500, new { message = "An error occurred while retrieving tags" });
        }
    }
}