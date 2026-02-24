namespace MyVideoArchive.Controllers;

[Authorize]
[Route("playlists")]
public class PlaylistsController : Controller
{
    private readonly IRepository<Playlist> playlistRepository;

    public PlaylistsController(IRepository<Playlist> playlistRepository)
    {
        this.playlistRepository = playlistRepository;
    }

    [Route("{id}")]
    public async Task<IActionResult> Details(int id)
    {
        var playlist = await playlistRepository.FindOneAsync(id);

        if (playlist is null)
        {
            return NotFound();
        }

        ViewBag.PlaylistId = id;

        return playlist.Platform == "Custom" ? View("DetailsCustom") : View();
    }
}