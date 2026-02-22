namespace MyVideoArchive.Controllers;

[Route("channels")]
public class ChannelsController : Controller
{
    private readonly IRepository<Channel> channelRepository;

    public ChannelsController(IRepository<Channel> channelRepository)
    {
        this.channelRepository = channelRepository;
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
}