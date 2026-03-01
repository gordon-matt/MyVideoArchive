namespace MyVideoArchive.Controllers;

[Authorize]
[Route("videos")]
public class VideosController : Controller
{
    private readonly IRepository<Video> videoRepository;

    public VideosController(IRepository<Video> videoRepository)
    {
        this.videoRepository = videoRepository;
    }

    [Route("")]
    public IActionResult Index() => View();

    [Route("{id}")]
    public async Task<IActionResult> Details(int id)
    {
        var video = await videoRepository.FindOneAsync(id);

        if (video is null)
        {
            return NotFound();
        }

        ViewBag.VideoId = id;

        return video.Platform == "Custom" ? View("DetailsCustom") : View();
    }
}