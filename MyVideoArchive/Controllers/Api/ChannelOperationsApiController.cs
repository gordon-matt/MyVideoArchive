using Ardalis.Result;

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

        return result.IsSuccess
            ? Ok(new { message = "Sync job queued successfully for all channels" })
            : result.Status switch
            {
                _ => StatusCode(500, new { message = "An error occurred while deleting the channel" })
            };
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Constants.Roles.Administrator)]
    public async Task<IActionResult> AdminDeleteChannel(
        int id,
        [FromQuery] bool deleteMetadata = false,
        [FromQuery] bool deleteFiles = false)
    {
        var result = await channelService.DeleteChannelAsync(id, deleteMetadata, deleteFiles);

        return result.IsSuccess
            ? NoContent()
            : result.Status switch
            {
                ResultStatus.NotFound => NotFound(new { message = "Channel not found" }),
                ResultStatus.Invalid => BadRequest(result.ValidationErrors),
                _ => StatusCode(500, new { message = "An error occurred while deleting the channel" })
            };
    }
}