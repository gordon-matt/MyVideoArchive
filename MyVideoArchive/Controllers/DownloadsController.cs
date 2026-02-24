namespace MyVideoArchive.Controllers;

[Authorize]
[Route("downloads")]
public class DownloadsController : Controller
{
    [Route("")]
    public IActionResult Index() => View();
}