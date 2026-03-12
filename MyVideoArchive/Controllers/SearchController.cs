namespace MyVideoArchive.Controllers;

[Authorize]
[Route("search")]
public class SearchController : Controller
{
    [Route("")]
    public IActionResult Index() => View();
}
