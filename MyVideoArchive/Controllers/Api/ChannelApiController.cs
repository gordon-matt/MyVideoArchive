using Ardalis.Result.AspNetCore;
using MyVideoArchive.Models.Requests.Channel;

namespace MyVideoArchive.Controllers.Api;

[Authorize]
[ApiController]
[Route("api/channels")]
public class ChannelApiController : ControllerBase
{
    private readonly IChannelService channelService;

    public ChannelApiController(IChannelService channelService)
    {
        this.channelService = channelService;
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