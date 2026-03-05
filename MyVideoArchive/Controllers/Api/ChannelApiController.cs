using Ardalis.Result.AspNetCore;

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
}