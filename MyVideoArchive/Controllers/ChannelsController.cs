using Microsoft.AspNetCore.Mvc;

namespace MyVideoArchive.Controllers;

[Route("channels")]
public class ChannelsController : Controller
{
    [Route("")]
    public IActionResult Index() => View();

    [Route("{id}")]
    public IActionResult Details(int id)
    {
        ViewBag.ChannelId = id;
        return View();
    }
}
