using Microsoft.AspNetCore.Mvc;

namespace MyVideoArchive.Controllers;

[Route("videos")]
public class VideosController : Controller
{
    [Route("{id}")]
    public IActionResult Details(int id)
    {
        ViewBag.VideoId = id;
        return View();
    }
}
