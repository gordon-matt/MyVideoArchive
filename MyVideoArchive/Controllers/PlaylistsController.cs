using Microsoft.AspNetCore.Mvc;

namespace MyVideoArchive.Controllers;

[Route("playlists")]
public class PlaylistsController : Controller
{
    [Route("{id}")]
    public IActionResult Details(int id)
    {
        ViewBag.PlaylistId = id;
        return View();
    }
}