using Microsoft.AspNetCore.Mvc;

namespace MyVideoArchive.Controllers;

[Route("downloads")]
public class DownloadsController : Controller
{
    [Route("")]
    public IActionResult Index() => View();
}