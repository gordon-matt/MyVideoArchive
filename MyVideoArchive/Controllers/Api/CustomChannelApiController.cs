namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for creating and managing custom (non-platform) channels
/// </summary>
[ApiController]
[Route("api/custom")]
[Authorize]
public class CustomChannelApiController : ControllerBase
{
    private readonly ILogger<CustomChannelApiController> logger;
    private readonly IUserContextService userContextService;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<Playlist> playlistRepository;
    private readonly IRepository<UserChannel> userChannelRepository;
    private readonly IRepository<Video> videoRepository;

    public CustomChannelApiController(
        ILogger<CustomChannelApiController> logger,
        IUserContextService userContextService,
        IRepository<Channel> channelRepository,
        IRepository<Playlist> playlistRepository,
        IRepository<UserChannel> userChannelRepository,
        IRepository<Video> videoRepository)
    {
        this.logger = logger;
        this.userContextService = userContextService;
        this.channelRepository = channelRepository;
        this.playlistRepository = playlistRepository;
        this.userChannelRepository = userChannelRepository;
        this.videoRepository = videoRepository;
    }

    /// <summary>
    /// Creates a new custom channel (not tied to any external platform)
    /// </summary>
    [HttpPost("channels")]
    public async Task<IActionResult> CreateChannel([FromBody] CreateCustomChannelRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            string channelId = Guid.NewGuid().ToString("N");
            var channel = new Channel
            {
                ChannelId = channelId,
                Name = request.Name,
                Description = request.Description,
                Url = $"custom://{channelId}",
                Platform = "Custom",
                SubscribedAt = DateTime.UtcNow
            };

            await channelRepository.InsertAsync(channel);

            await userChannelRepository.InsertAsync(new UserChannel
            {
                UserId = userId,
                ChannelId = channel.Id,
                SubscribedAt = DateTime.UtcNow
            });

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Created custom channel {ChannelId} for user {UserId}", channel.Id, userId);
            }

            return Ok(new { channel.Id, channel.Name, channel.Platform });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error creating custom channel");
            }

            return StatusCode(500, new { message = "An error occurred while creating the custom channel" });
        }
    }

    /// <summary>
    /// Updates metadata for a custom channel
    /// </summary>
    [HttpPut("channels/{channelId:int}")]
    public async Task<IActionResult> UpdateChannel(int channelId, [FromBody] UpdateCustomChannelRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var channel = await channelRepository.FindOneAsync(channelId);
            if (channel is null || channel.Platform != "Custom")
            {
                return NotFound();
            }

            var hasAccess = await userChannelRepository.FindOneAsync(new SearchOptions<UserChannel>
            {
                Query = x => x.UserId == userId && x.ChannelId == channelId
            });

            if (hasAccess is null && !userContextService.IsAdministrator())
            {
                return Forbid();
            }

            channel.Name = request.Name;
            channel.Description = request.Description;
            channel.ThumbnailUrl = request.ThumbnailUrl;

            await channelRepository.UpdateAsync(channel);

            return Ok();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error updating custom channel {ChannelId}", channelId);
            }

            return StatusCode(500, new { message = "An error occurred while updating the channel" });
        }
    }

    /// <summary>
    /// Creates a new playlist within a custom channel
    /// </summary>
    [HttpPost("channels/{channelId:int}/playlists")]
    public async Task<IActionResult> CreatePlaylist(int channelId, [FromBody] CreateCustomPlaylistRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var channel = await channelRepository.FindOneAsync(channelId);
            if (channel is null || channel.Platform != "Custom")
            {
                return NotFound();
            }

            var hasAccess = await userChannelRepository.FindOneAsync(new SearchOptions<UserChannel>
            {
                Query = x => x.UserId == userId && x.ChannelId == channelId
            });

            if (hasAccess is null && !userContextService.IsAdministrator())
            {
                return Forbid();
            }

            string playlistId = Guid.NewGuid().ToString("N");
            var playlist = new Playlist
            {
                PlaylistId = playlistId,
                Name = request.Name,
                Description = request.Description,
                Url = $"custom://{playlistId}",
                Platform = "Custom",
                ChannelId = channelId,
                SubscribedAt = DateTime.UtcNow
            };

            await playlistRepository.InsertAsync(playlist);

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Created custom playlist {PlaylistId} in channel {ChannelId}", playlist.Id, channelId);
            }

            return Ok(new { playlist.Id, playlist.Name });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error creating custom playlist in channel {ChannelId}", channelId);
            }

            return StatusCode(500, new { message = "An error occurred while creating the playlist" });
        }
    }

    /// <summary>
    /// Updates metadata for a custom playlist
    /// </summary>
    [HttpPut("playlists/{playlistId:int}")]
    public async Task<IActionResult> UpdatePlaylist(int playlistId, [FromBody] UpdateCustomPlaylistRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var playlist = await playlistRepository.FindOneAsync(playlistId);
            if (playlist is null)
            {
                return NotFound();
            }

            var hasAccess = await userChannelRepository.FindOneAsync(new SearchOptions<UserChannel>
            {
                Query = x => x.UserId == userId && x.ChannelId == playlist.ChannelId
            });

            if (hasAccess is null && !userContextService.IsAdministrator())
            {
                return Forbid();
            }

            playlist.Name = request.Name;
            playlist.Description = request.Description;

            await playlistRepository.UpdateAsync(playlist);

            return Ok();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error updating custom playlist {PlaylistId}", playlistId);
            }

            return StatusCode(500, new { message = "An error occurred while updating the playlist" });
        }
    }

    /// <summary>
    /// Updates metadata for a custom video
    /// </summary>
    [HttpPut("videos/{videoId:int}")]
    public async Task<IActionResult> UpdateVideo(int videoId, [FromBody] UpdateCustomVideoRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var video = await videoRepository.FindOneAsync(new SearchOptions<Video>
            {
                Query = x => x.Id == videoId,
                Include = query => query.Include(x => x.Channel)
            });

            if (video is null)
            {
                return NotFound();
            }

            var hasAccess = await userChannelRepository.FindOneAsync(new SearchOptions<UserChannel>
            {
                Query = x => x.UserId == userId && x.ChannelId == video.ChannelId
            });

            if (hasAccess is null && !userContextService.IsAdministrator())
            {
                return Forbid();
            }

            video.Title = request.Title;
            video.Description = request.Description;
            video.ThumbnailUrl = request.ThumbnailUrl;
            video.UploadDate = request.UploadDate;
            video.Duration = request.Duration;

            if (!string.IsNullOrEmpty(request.FilePath))
            {
                video.FilePath = request.FilePath;
                if (System.IO.File.Exists(request.FilePath))
                {
                    video.FileSize = new FileInfo(request.FilePath).Length;
                    video.DownloadedAt ??= DateTime.UtcNow;
                }
            }

            // If metadata is now complete, clear the review flag
            if (!string.IsNullOrEmpty(video.Title) && video.Title != Path.GetFileNameWithoutExtension(video.FilePath ?? ""))
            {
                video.NeedsMetadataReview = false;
            }

            await videoRepository.UpdateAsync(video);

            return Ok();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error updating video {VideoId}", videoId);
            }

            return StatusCode(500, new { message = "An error occurred while updating the video" });
        }
    }

    public record CreateCustomChannelRequest(string Name, string? Description);

    public record CreateCustomPlaylistRequest(string Name, string? Description);

    public record UpdateCustomChannelRequest(string Name, string? Description, string? ThumbnailUrl);

    public record UpdateCustomPlaylistRequest(string Name, string? Description);

    public record UpdateCustomVideoRequest(
        string Title,
        string? Description,
        string? ThumbnailUrl,
        DateTime? UploadDate,
        TimeSpan? Duration,
        string? FilePath);
}