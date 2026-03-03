using MyVideoArchive.Models.Requests.Playlist;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for managing channel playlists
/// </summary>
[Authorize]
[ApiController]
[Route("api/channels/{channelId}/playlists")]
public class ChannelPlaylistsApiController : ControllerBase
{
    private readonly IPlaylistService playlistService;

    public ChannelPlaylistsApiController(IPlaylistService playlistService)
    {
        this.playlistService = playlistService;
    }

    /// <summary>
    /// Get all playlists for a channel (available, subscribed, and ignored), paginated
    /// </summary>
    [HttpGet("available")]
    public async Task<IActionResult> GetAvailablePlaylists(
        int channelId,
        [FromQuery] bool showIgnored = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 24)
    {
        var result = await playlistService.GetAvailablePlaylistsAsync(
            channelId,
            showIgnored,
            page,
            pageSize,
            HttpContext.RequestAborted);

        return result.ToActionResult(this, value => Ok(new
        {
            playlists = value,
            pagination = new
            {
                currentPage = page,
                pageSize,
                totalCount = value.ItemCount,
                totalPages = value.PageCount
            }
        }));
    }

    /// <summary>
    /// Subscribe to selected playlists
    /// </summary>
    [HttpPost("subscribe")]
    public async Task<IActionResult> SubscribePlaylists(int channelId, [FromBody] SubscribePlaylistsRequest request)
    {
        var result = await playlistService.SubscribePlaylistsAsync(channelId, request);

        return result.ToActionResult(this, value => Ok(new
        {
            message = value.Message,
            subscribedCount = value.SubscribedCount
        }));
    }

    /// <summary>
    /// Subscribe to all playlists for a channel
    /// </summary>
    [HttpPost("subscribe-all")]
    public async Task<IActionResult> SubscribeAllPlaylists(int channelId)
    {
        var result = await playlistService.SubscribeAllPlaylistsAsync(channelId);

        return result.ToActionResult(this, value => Ok(new
        {
            message = value.Message,
            subscribedCount = value.SubscribedCount
        }));
    }

    /// <summary>
    /// Toggle ignore status for a playlist
    /// </summary>
    [HttpPut("{playlistId}/ignore")]
    public async Task<IActionResult> ToggleIgnore(int channelId, int playlistId, [FromBody] IgnorePlaylistRequest request)
    {
        var result = await playlistService.ToggleIgnoreAsync(channelId, playlistId, request);

        return result.ToActionResult(this, value => Ok(new
        {
            message = value.Message,
            isIgnored = value.IsIgnored
        }));
    }

    /// <summary>
    /// Refresh playlists from the platform for a channel
    /// </summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshPlaylists(int channelId)
    {
        var result = await playlistService.RefreshPlaylistsAsync(channelId, HttpContext.RequestAborted);

        return result.ToActionResult(this, value => Ok(new
        {
            message = value.Message,
            totalCount = value.TotalCount,
            newCount = value.NewCount
        }));
    }
}