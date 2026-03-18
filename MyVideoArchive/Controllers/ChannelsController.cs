namespace MyVideoArchive.Controllers;

[Authorize]
[Route("channels")]
public class ChannelsController : Controller
{
    private readonly IRepository<Channel> channelRepository;
    private readonly IChannelService channelService;

    public ChannelsController(
        IRepository<Channel> channelRepository,
        IChannelService channelService)
    {
        this.channelRepository = channelRepository;
        this.channelService = channelService;
    }

    [Route("")]
    public IActionResult Index() => View();

    [Route("{id}")]
    public async Task<IActionResult> Details(int id)
    {
        var channel = await channelRepository.FindOneAsync(id);

        if (channel is null)
        {
            return NotFound();
        }

        ViewBag.ChannelId = id;

        return channel.Platform == "Custom" ? View("DetailsCustom") : View();
    }

    [HttpPost("{id:int}/sync")]
    [ValidateAntiForgeryToken]
    public IActionResult Sync(int id)
    {
        var result = channelService.SyncChannel(id);
        return result.ToActionResult(this, () => RedirectToAction(nameof(Details), new { id }));
    }
}