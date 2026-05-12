namespace MyVideoArchive.Controllers;

[Authorize]
[Route("series")]
public class SeriesController : Controller
{
    private readonly ISeriesService seriesService;

    public SeriesController(ISeriesService seriesService)
    {
        this.seriesService = seriesService;
    }

    [Route("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var result = await seriesService.GetSeriesAsync(id, HttpContext.RequestAborted);

        if (!result.IsSuccess)
        {
            return NotFound();
        }

        ViewBag.SeriesId = id;
        return View();
    }
}
