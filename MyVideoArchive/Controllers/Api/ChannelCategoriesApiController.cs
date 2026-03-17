using MyVideoArchive.Data.Entities;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API for managing per-user channel categories.
/// </summary>
[Authorize]
[ApiController]
[Route("api/channel-categories")]
public class ChannelCategoriesApiController : ControllerBase
{
    private readonly IUserContextService userContextService;
    private readonly IRepository<ChannelCategory> categoryRepository;
    private readonly IRepository<UserChannel> userChannelRepository;

    public ChannelCategoriesApiController(
        IUserContextService userContextService,
        IRepository<ChannelCategory> categoryRepository,
        IRepository<UserChannel> userChannelRepository)
    {
        this.userContextService = userContextService;
        this.categoryRepository = categoryRepository;
        this.userChannelRepository = userChannelRepository;
    }

    /// <summary>
    /// Returns all categories belonging to the current user.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetCategories()
    {
        string? userId = userContextService.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var categories = await categoryRepository.FindAsync(
            new SearchOptions<ChannelCategory>
            {
                Query = x => x.UserId == userId,
                OrderBy = q => q.OrderBy(x => x.Name)
            });

        return Ok(categories.Select(c => new
        {
            id = c.Id,
            name = c.Name,
            thumbnailUrl = c.ThumbnailUrl
        }));
    }

    /// <summary>
    /// Creates a new category for the current user.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateCategory([FromBody] CreateChannelCategoryRequest request)
    {
        string? userId = userContextService.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Category name is required." });
        }

        bool exists = await categoryRepository.ExistsAsync(x =>
            x.UserId == userId &&
            x.Name.ToLower() == request.Name.Trim().ToLower());

        if (exists)
        {
            return Conflict(new { message = "A category with this name already exists." });
        }

        var category = await categoryRepository.InsertAsync(new ChannelCategory
        {
            UserId = userId,
            Name = request.Name.Trim()
        });

        return Ok(new { id = category.Id, name = category.Name, thumbnailUrl = category.ThumbnailUrl });
    }

    /// <summary>
    /// Deletes a category. Channels in this category have their CategoryId set to null.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        string? userId = userContextService.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var category = await categoryRepository.FindOneAsync(new SearchOptions<ChannelCategory>
        {
            Query = x => x.Id == id && x.UserId == userId
        });

        if (category is null)
        {
            return NotFound();
        }

        // Clear category assignment on all user channels in this category (cascade via DB is SetNull,
        // but we handle it explicitly here to support repositories that don't apply FK cascade).
        var userChannels = await userChannelRepository.FindAsync(new SearchOptions<UserChannel>
        {
            Query = x => x.UserId == userId && x.CategoryId == id
        });

        foreach (var uc in userChannels)
        {
            uc.CategoryId = null;
        }

        if (userChannels.Count > 0)
        {
            await userChannelRepository.UpdateAsync(userChannels);
        }

        await categoryRepository.DeleteAsync(category);

        return NoContent();
    }
}

public class CreateChannelCategoryRequest
{
    public string Name { get; set; } = string.Empty;
}
