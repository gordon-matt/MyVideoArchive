namespace MyVideoArchive.Controllers;

[Authorize(Roles = Constants.Roles.Administrator)]
[Route("downloads")]
public class DownloadsController : Controller
{
    [Route("")]
    public IActionResult Index() => View();
}