using Ardalis.Result.AspNetCore;
using MyVideoArchive.Data.Entities;
using MyVideoArchive.Models.Requests.Channel;

namespace MyVideoArchive.Controllers.Api;

[Authorize]
[ApiController]
[Route("api/channels")]
public class ChannelApiController : ControllerBase
{
    private readonly IChannelService channelService;
    private readonly IUserContextService userContextService;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<UserChannel> userChannelRepository;
    private readonly IRepository<ChannelCategory> categoryRepository;

    public ChannelApiController(
        IChannelService channelService,
        IUserContextService userContextService,
        IRepository<Channel> channelRepository,
        IRepository<UserChannel> userChannelRepository,
        IRepository<ChannelCategory> categoryRepository)
    {
        this.channelService = channelService;
        this.userContextService = userContextService;
        this.channelRepository = channelRepository;
        this.userChannelRepository = userChannelRepository;
        this.categoryRepository = categoryRepository;
    }

    /// <summary>
    /// Returns the current user's subscribed channels with category info.
    /// Admins see all channels (without category info, since categories are per-user).
    /// </summary>
    [HttpGet("my-channels")]
    public async Task<IActionResult> GetMyChannels([FromQuery] string? platform = null)
    {
        string? userId = userContextService.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        bool isAdmin = userContextService.IsAdministrator();

        if (isAdmin)
        {
            var allChannels = await channelRepository.FindAsync(new SearchOptions<Channel>
            {
                Query = x => platform == null || x.Platform == platform,
                OrderBy = q => q.OrderBy(x => x.Name)
            });

            return Ok(allChannels.Select(c => new
            {
                id = c.Id,
                channelId = c.ChannelId,
                name = c.Name,
                url = c.Url,
                avatarUrl = c.AvatarUrl,
                bannerUrl = c.BannerUrl,
                platform = c.Platform,
                subscribedAt = c.SubscribedAt,
                categoryId = (int?)null
            }));
        }

        var userChannels = await userChannelRepository.FindAsync(new SearchOptions<UserChannel>
        {
            Query = x =>
                x.UserId == userId &&
                (platform == null || x.Channel.Platform == platform),
            Include = q => q.Include(x => x.Channel)
        });

        var result = userChannels
            .OrderBy(uc => uc.Channel.Name)
            .Select(uc => new
            {
                id = uc.Channel.Id,
                channelId = uc.Channel.ChannelId,
                name = uc.Channel.Name,
                url = uc.Channel.Url,
                avatarUrl = uc.Channel.AvatarUrl,
                bannerUrl = uc.Channel.BannerUrl,
                platform = uc.Channel.Platform,
                subscribedAt = uc.SubscribedAt,
                categoryId = uc.CategoryId
            });

        return Ok(result);
    }

    /// <summary>
    /// Assigns (or clears) a category on the current user's subscription to a channel.
    /// </summary>
    [HttpPut("{id:int}/category")]
    public async Task<IActionResult> AssignCategory(int id, [FromBody] AssignChannelCategoryRequest request)
    {
        string? userId = userContextService.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var userChannel = await userChannelRepository.FindOneAsync(new SearchOptions<UserChannel>
        {
            Query = x => x.UserId == userId && x.ChannelId == id
        });

        if (userChannel is null)
        {
            return NotFound();
        }

        if (request.CategoryId is not null)
        {
            bool categoryExists = await categoryRepository.ExistsAsync(x =>
                x.Id == request.CategoryId.Value && x.UserId == userId);

            if (!categoryExists)
            {
                return BadRequest(new { message = "Category not found." });
            }
        }

        userChannel.CategoryId = request.CategoryId;
        await userChannelRepository.UpdateAsync(userChannel);

        return Ok(new { message = "Category assigned." });
    }

    [HttpPost("sync-all")]
    public IActionResult SyncAllChannels()
    {
        var result = channelService.SyncAllChannels();
        return result.ToActionResult(this, () => Ok(new { message = "Sync job queued successfully for all channels" }));
    }

    [HttpGet("{id:int}/subscribers")]
    [Authorize(Roles = Constants.Roles.Administrator)]
    public async Task<IActionResult> GetChannelSubscribers(int id, CancellationToken cancellationToken = default)
    {
        var result = await channelService.GetChannelSubscribersAsync(id, cancellationToken);
        return result.ToActionResult(this, value => Ok(new { subscribers = value, count = value.Count }));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Constants.Roles.Administrator)]
    public async Task<IActionResult> AdminDeleteChannel(
        int id,
        [FromQuery] bool deleteMetadata = false,
        [FromQuery] bool deleteFiles = false)
    {
        var result = await channelService.DeleteChannelAsync(id, deleteMetadata, deleteFiles);
        return result.ToActionResult(this, NoContent);
    }

    [HttpGet("{id:int}/sync-status")]
    public async Task<IActionResult> GetSyncStatus(int id, CancellationToken cancellationToken)
    {
        bool? isSyncing = await channelService.GetSyncStatusAsync(id, cancellationToken);
        return isSyncing is null ? NotFound() : Ok(new { isSyncing = isSyncing.Value });
    }

    /// <summary>
    /// Returns all users with their subscription status for a channel. Admin only.
    /// </summary>
    [HttpGet("{id:int}/user-subscriptions")]
    [Authorize(Roles = Constants.Roles.Administrator)]
    public async Task<IActionResult> GetUserSubscriptions(int id, CancellationToken cancellationToken)
    {
        var result = await channelService.GetUserSubscriptionsAsync(id, cancellationToken);
        return result.ToActionResult(this, value => Ok(new { users = value }));
    }

    /// <summary>
    /// Updates user subscriptions for a channel (admin assigns/removes users). Admin only.
    /// </summary>
    [HttpPut("{id:int}/user-subscriptions")]
    [Authorize(Roles = Constants.Roles.Administrator)]
    public async Task<IActionResult> UpdateUserSubscriptions(int id, [FromBody] UpdateChannelUserSubscriptionsRequest request)
    {
        var result = await channelService.UpdateUserSubscriptionsAsync(id, request.SubscribedUserIds);
        return result.ToActionResult(this, () => Ok(new { message = "User subscriptions updated successfully." }));
    }
}

public class AssignChannelCategoryRequest
{
    public int? CategoryId { get; set; }
}