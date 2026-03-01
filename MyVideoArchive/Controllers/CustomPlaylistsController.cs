namespace MyVideoArchive.Controllers;

[Authorize]
[Route("my-playlists")]
public class CustomPlaylistsController : Controller
{
    [Route("")]
    public IActionResult Index() => View();

    [Route("{id}")]
    public IActionResult Details(int id)
    {
        ViewBag.PlaylistId = id;
        return View();
    }
}