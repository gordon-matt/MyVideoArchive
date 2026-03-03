namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for tracking user video watch state
/// </summary>
[Authorize]
[ApiController]
[Route("api/user/videos")]
public class UserVideoApiController : ControllerBase
{
    private readonly IVideoService videoService;

    public UserVideoApiController(IVideoService videoService)
    {
        this.videoService = videoService;
    }

    /// <summary>
    /// Returns the IDs of videos (from the supplied list) that the current user has watched
    /// </summary>
    [HttpGet("watched")]
    public async Task<IActionResult> GetWatchedVideoIds([FromQuery] int[] videoIds)
    {
        var result = await videoService.GetWatchedVideoIdsAsync(videoIds);

        return result.ToActionResult(this, value => Ok(new { watchedIds = value.WatchedIds }));
    }

    /// <summary>
    /// Returns the IDs of videos in the specified playlist that the current user has watched
    /// </summary>
    [HttpGet("watched/by-playlist/{playlistId:int}")]
    public async Task<IActionResult> GetWatchedVideoIdsByPlaylist(int playlistId)
    {
        var result = await videoService.GetWatchedVideoIdsByPlaylistAsync(playlistId);

        return result.ToActionResult(this, value => Ok(new { watchedIds = value.WatchedIds }));
    }

    /// <summary>
    /// Returns the IDs of videos for the specified channel that the current user has watched
    /// </summary>
    [HttpGet("watched/by-channel/{channelId:int}")]
    public async Task<IActionResult> GetWatchedVideoIdsByChannel(int channelId)
    {
        var result = await videoService.GetWatchedVideoIdsByChannelAsync(channelId);

        return result.ToActionResult(this, value => Ok(new { watchedIds = value.WatchedIds }));
    }

    /// <summary>
    /// Mark a video as watched for the current user
    /// </summary>
    [HttpPost("{videoId}/watched")]
    public async Task<IActionResult> MarkWatched(int videoId)
    {
        var result = await videoService.MarkWatchedAsync(videoId);

        return result.ToActionResult(this, () => Ok(new { message = "Video marked as watched" }));
    }

    /// <summary>
    /// Mark a video as unwatched for the current user
    /// </summary>
    [HttpDelete("{videoId}/watched")]
    public async Task<IActionResult> MarkUnwatched(int videoId)
    {
        var result = await videoService.MarkUnwatchedAsync(videoId);

        return result.ToActionResult(this, () => Ok(new { message = "Video marked as unwatched" }));
    }
}