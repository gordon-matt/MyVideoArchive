using MyVideoArchive.Models.Requests.Series;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for managing series (grouped playlists within a channel).
/// </summary>
[ApiController]
[Route("api/channels/{channelId:int}/series")]
[Authorize]
public class SeriesApiController : ControllerBase
{
    private readonly ISeriesService seriesService;

    public SeriesApiController(ISeriesService seriesService)
    {
        this.seriesService = seriesService;
    }

    [HttpGet]
    public async Task<IActionResult> GetSeries(int channelId)
    {
        var result = await seriesService.GetSeriesForChannelAsync(channelId, HttpContext.RequestAborted);
        return result.ToActionResult(this, value => Ok(new { series = value }));
    }

    [HttpPost]
    public async Task<IActionResult> CreateSeries(int channelId, [FromBody] CreateSeriesRequest request)
    {
        var result = await seriesService.CreateSeriesAsync(channelId, request, HttpContext.RequestAborted);
        return result.ToActionResult(this, value => Ok(new { series = value }));
    }
}

[ApiController]
[Route("api/series")]
[Authorize]
public class SeriesManagementApiController : ControllerBase
{
    private readonly ISeriesService seriesService;

    public SeriesManagementApiController(ISeriesService seriesService)
    {
        this.seriesService = seriesService;
    }

    [HttpGet("{seriesId:int}")]
    public async Task<IActionResult> GetSeries(int seriesId)
    {
        var result = await seriesService.GetSeriesAsync(seriesId, HttpContext.RequestAborted);
        return result.ToActionResult(this, value => Ok(value));
    }

    [HttpPut("{seriesId:int}")]
    public async Task<IActionResult> UpdateSeries(int seriesId, [FromBody] UpdateSeriesRequest request)
    {
        var result = await seriesService.UpdateSeriesAsync(seriesId, request, HttpContext.RequestAborted);
        return result.ToActionResult(this, Ok);
    }

    [HttpPut("{seriesId:int}/playlists")]
    public async Task<IActionResult> UpdateSeriesPlaylists(int seriesId, [FromBody] UpdateSeriesPlaylistsRequest request)
    {
        var result = await seriesService.UpdateSeriesPlaylistsAsync(seriesId, request, HttpContext.RequestAborted);
        return result.ToActionResult(this, Ok);
    }

    [HttpDelete("{seriesId:int}")]
    public async Task<IActionResult> DeleteSeries(int seriesId)
    {
        var result = await seriesService.DeleteSeriesAsync(seriesId, HttpContext.RequestAborted);
        return result.ToActionResult(this, NoContent);
    }
}

[ApiController]
[Route("api/playlists/{playlistId:int}/series")]
[Authorize]
public class PlaylistSeriesApiController : ControllerBase
{
    private readonly ISeriesService seriesService;

    public PlaylistSeriesApiController(ISeriesService seriesService)
    {
        this.seriesService = seriesService;
    }

    [HttpGet]
    public async Task<IActionResult> GetSeriesForPlaylist(int playlistId)
    {
        var result = await seriesService.GetSeriesForPlaylistAsync(playlistId, HttpContext.RequestAborted);
        return result.ToActionResult(this, value => Ok(new { series = value }));
    }
}
