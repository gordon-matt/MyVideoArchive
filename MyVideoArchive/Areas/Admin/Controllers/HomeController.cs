namespace MyVideoArchive.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin")]
[Authorize(Roles = Constants.Roles.Administrator)]
public class HomeController : Controller
{
    [Route("")]
    public IActionResult Index() => View();
}
