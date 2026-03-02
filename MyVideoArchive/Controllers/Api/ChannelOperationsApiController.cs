namespace MyVideoArchive.Controllers.Api;

[Authorize]
[ApiController]
[Route("api/channels")]
public class ChannelOperationsApiController : ControllerBase
{
    private readonly IChannelService channelService;

    public ChannelOperationsApiController(IChannelService channelService)
    {
        this.channelService = channelService;
    }

    [HttpPost("sync-all")]
    public IActionResult SyncAllChannels()
    {
        var result = channelService.SyncAllChannels();
        return result.ToActionResult(this, () => Ok(new { message = "Sync job queued successfully for all channels" }));
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