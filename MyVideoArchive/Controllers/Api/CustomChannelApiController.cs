namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for creating and managing custom (non-platform) channels, playlists and videos
/// </summary>
[ApiController]
[Route("api/custom")]
[Authorize]
public class CustomChannelApiController : ControllerBase
{
    private static readonly string[] AllowedImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    private readonly ILogger<CustomChannelApiController> logger;
    private readonly IConfiguration configuration;
    private readonly IUserContextService userContextService;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<Playlist> playlistRepository;
    private readonly IRepository<PlaylistVideo> playlistVideoRepository;
    private readonly IRepository<UserChannel> userChannelRepository;
    private readonly IRepository<Video> videoRepository;

    public CustomChannelApiController(
        ILogger<CustomChannelApiController> logger,
        IConfiguration configuration,
        IUserContextService userContextService,
        IRepository<Channel> channelRepository,
        IRepository<Playlist> playlistRepository,
        IRepository<PlaylistVideo> playlistVideoRepository,
        IRepository<UserChannel> userChannelRepository,
        IRepository<Video> videoRepository)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.userContextService = userContextService;
        this.channelRepository = channelRepository;
        this.playlistRepository = playlistRepository;
        this.playlistVideoRepository = playlistVideoRepository;
        this.userChannelRepository = userChannelRepository;
        this.videoRepository = videoRepository;
    }

    // ── Channels ────────────────────────────────────────────────────────────

    /// <summary>Creates a new custom channel not tied to any external platform.</summary>
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

    /// <summary>Updates name, description and thumbnail URL for a custom channel.</summary>
    [HttpPut("channels/{channelId:int}")]
    public async Task<IActionResult> UpdateChannel(int channelId, [FromBody] UpdateCustomChannelRequest request)
    {
        try
        {
            var (canAccess, channel) = await CanAccessChannel(channelId);
            if (!canAccess)
            {
                return Forbid();
            }

            if (channel is null || channel.Platform != "Custom")
            {
                return NotFound();
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

    /// <summary>Serves the locally stored thumbnail for a custom channel.</summary>
    [HttpGet("channels/{channelId:int}/thumbnail")]
    public async Task<IActionResult> GetChannelThumbnail(int channelId)
    {
        var (canAccess, channel) = await CanAccessChannel(channelId);
        if (!canAccess)
        {
            return Forbid();
        }

        if (channel is null || channel.Platform != "Custom")
        {
            return NotFound();
        }

        string dir = GetCustomChannelThumbnailDirectory(channel);
        return ServeImageFromDirectory(dir, "channel");
    }

    /// <summary>Accepts an uploaded image and saves it as the channel thumbnail (file: channel + extension).</summary>
    [HttpPost("channels/{channelId:int}/thumbnail")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB
    public async Task<IActionResult> UploadChannelThumbnail(int channelId, IFormFile file)
    {
        try
        {
            var (canAccess, channel) = await CanAccessChannel(channelId);
            if (!canAccess)
            {
                return Forbid();
            }

            if (channel is null || channel.Platform != "Custom")
            {
                return NotFound();
            }

            string dir = GetCustomChannelThumbnailDirectory(channel);
            Directory.CreateDirectory(dir);

            string ext = NormaliseImageExtension(Path.GetExtension(file.FileName));
            string thumbPath = Path.Combine(dir, "channel" + ext);

            await using (var stream = System.IO.File.Create(thumbPath))
            {
                await file.CopyToAsync(stream);
            }

            // Remove any previous thumbnail with other extension
            foreach (string other in AllowedImageExtensions)
            {
                if (other == ext) continue;
                string otherPath = Path.Combine(dir, "channel" + other);
                if (System.IO.File.Exists(otherPath))
                {
                    System.IO.File.Delete(otherPath);
                    break;
                }
            }

            channel.ThumbnailUrl = $"/api/custom/channels/{channelId}/thumbnail";
            await channelRepository.UpdateAsync(channel);

            return Ok(new { thumbnailUrl = channel.ThumbnailUrl });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error uploading thumbnail for channel {ChannelId}", channelId);
            }

            return StatusCode(500, new { message = "An error occurred while saving the thumbnail" });
        }
    }

    // ── Playlists ────────────────────────────────────────────────────────────

    /// <summary>Creates a new playlist within a custom channel.</summary>
    [HttpPost("channels/{channelId:int}/playlists")]
    public async Task<IActionResult> CreatePlaylist(int channelId, [FromBody] CreateCustomPlaylistRequest request)
    {
        try
        {
            var (canAccess, channel) = await CanAccessChannel(channelId);
            if (!canAccess)
            {
                return Forbid();
            }

            if (channel is null || channel.Platform != "Custom")
            {
                return NotFound();
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

    /// <summary>Updates name and description for a custom playlist.</summary>
    [HttpPut("playlists/{playlistId:int}")]
    public async Task<IActionResult> UpdatePlaylist(int playlistId, [FromBody] UpdateCustomPlaylistRequest request)
    {
        try
        {
            var playlist = await playlistRepository.FindOneAsync(playlistId);
            if (playlist is null)
            {
                return NotFound();
            }

            var (canAccess, channel) = await CanAccessChannel(playlist.ChannelId);
            if (!canAccess)
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
    /// Deletes a custom playlist and all of its playlist–video junction records.
    /// PlaylistVideo uses ClientNoAction, so we delete junction rows manually first.
    /// </summary>
    [HttpDelete("playlists/{playlistId:int}")]
    public async Task<IActionResult> DeletePlaylist(int playlistId)
    {
        try
        {
            var playlist = await playlistRepository.FindOneAsync(playlistId);
            if (playlist is null)
            {
                return NotFound();
            }

            var (canAccess, channel) = await CanAccessChannel(playlist.ChannelId);
            if (!canAccess)
            {
                return Forbid();
            }

            // Delete junction records first (no cascade configured)
            var playlistVideos = await playlistVideoRepository.FindAsync(new SearchOptions<PlaylistVideo>
            {
                Query = x => x.PlaylistId == playlistId
            });

            await playlistVideoRepository.DeleteAsync(playlistVideos);

            // Delete thumbnail file if it was stored locally
            await TryDeletePlaylistThumbnailFileAsync(playlist);

            await playlistRepository.DeleteAsync(playlist);
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Deleted custom playlist {PlaylistId}", playlistId);
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error deleting playlist {PlaylistId}", playlistId);
            }

            return StatusCode(500, new { message = "An error occurred while deleting the playlist" });
        }
    }

    /// <summary>Serves the locally stored thumbnail for a playlist.</summary>
    [HttpGet("playlists/{playlistId:int}/thumbnail")]
    public async Task<IActionResult> GetPlaylistThumbnail(int playlistId)
    {
        var playlist = await playlistRepository.FindOneAsync(new SearchOptions<Playlist>
        {
            Query = x => x.Id == playlistId,
            Include = query => query.Include(x => x.Channel)
        });
        if (playlist is null)
        {
            return NotFound();
        }

        string dir = GetPlaylistThumbnailDirectory(playlist);
        return ServeImageFromDirectory(dir, playlist.PlaylistId);
    }

    /// <summary>Accepts an uploaded image and saves it as the playlist thumbnail.</summary>
    [HttpPost("playlists/{playlistId:int}/thumbnail")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB
    public async Task<IActionResult> UploadPlaylistThumbnail(int playlistId, IFormFile file)
    {
        try
        {
            var playlist = await playlistRepository.FindOneAsync(new SearchOptions<Playlist>
            {
                Query = x => x.Id == playlistId,
                Include = query => query.Include(x => x.Channel)
            });

            if (playlist is null)
            {
                return NotFound();
            }

            var (canAccess, channel) = await CanAccessChannel(playlist.ChannelId);
            if (!canAccess)
            {
                return Forbid();
            }

            string dir = GetPlaylistThumbnailDirectory(playlist);
            Directory.CreateDirectory(dir);

            string ext = NormaliseImageExtension(Path.GetExtension(file.FileName));
            string thumbPath = Path.Combine(dir, playlist.PlaylistId + ext);

            await using (var stream = System.IO.File.Create(thumbPath))
            {
                await file.CopyToAsync(stream);
            }

            // Use a cache-busting URL so the browser fetches the new image
            playlist.ThumbnailUrl = $"/api/custom/playlists/{playlistId}/thumbnail";
            await playlistRepository.UpdateAsync(playlist);

            return Ok(new { thumbnailUrl = playlist.ThumbnailUrl });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error uploading thumbnail for playlist {PlaylistId}", playlistId);
            }

            return StatusCode(500, new { message = "An error occurred while saving the thumbnail" });
        }
    }

    /// <summary>Returns all playlists belonging to a custom channel.</summary>
    [HttpGet("channels/{channelId:int}/playlists")]
    public async Task<IActionResult> GetChannelPlaylists(int channelId)
    {
        var (canAccess, channel) = await CanAccessChannel(channelId);
        if (!canAccess)
        {
            return Forbid();
        }

        if (channel is null || channel.Platform != "Custom")
        {
            return NotFound();
        }

        var playlists = await playlistRepository.FindAsync(new SearchOptions<Playlist>
        {
            Query = x => x.ChannelId == channelId && !x.IsIgnored
        }, x => new { x.Id, x.Name });

        return Ok(playlists);
    }

    /// <summary>Returns the IDs of custom playlists that contain the given video.</summary>
    [HttpGet("videos/{videoId:int}/playlists")]
    public async Task<IActionResult> GetVideoPlaylists(int videoId)
    {
        var video = await videoRepository.FindOneAsync(videoId);
        if (video is null)
        {
            return NotFound();
        }

        var (canAccess, _) = await CanAccessChannel(video.ChannelId);
        if (!canAccess)
        {
            return Forbid();
        }

        var entries = await playlistVideoRepository.FindAsync(new SearchOptions<PlaylistVideo>
        {
            Query = x => x.VideoId == videoId
        }, x => x.PlaylistId);

        return Ok(entries);
    }

    // ── Videos ────────────────────────────────────────────────────────────────

    /// <summary>Updates metadata for a custom (or manually imported) video.</summary>
    [HttpPut("videos/{videoId:int}")]
    public async Task<IActionResult> UpdateVideo(int videoId, [FromBody] UpdateCustomVideoRequest request)
    {
        try
        {
            var video = await videoRepository.FindOneAsync(new SearchOptions<Video>
            {
                Query = x => x.Id == videoId,
                Include = query => query.Include(x => x.Channel)
            });
            if (video is null)
            {
                return NotFound();
            }

            var (canAccess, channel) = await CanAccessChannel(video.ChannelId);
            if (!canAccess)
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

            // Clear review flag when a meaningful title has been provided
            if (!string.IsNullOrEmpty(video.Title) &&
                !string.Equals(video.Title, video.VideoId, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(video.Title, Path.GetFileNameWithoutExtension(video.FilePath ?? ""), StringComparison.OrdinalIgnoreCase))
            {
                video.NeedsMetadataReview = false;
            }

            await videoRepository.UpdateAsync(video);

            // Sync playlist memberships when a list is provided (only touches playlists in this channel)
            if (request.PlaylistIds is not null)
            {
                await SyncVideoPlaylistsAsync(video.Id, video.ChannelId, request.PlaylistIds);
            }

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

    /// <summary>Serves the locally stored thumbnail for a video.</summary>
    [HttpGet("videos/{videoId:int}/thumbnail")]
    public async Task<IActionResult> GetVideoThumbnail(int videoId)
    {
        var video = await videoRepository.FindOneAsync(videoId);
        if (video is null || string.IsNullOrEmpty(video.FilePath))
        {
            return NotFound();
        }

        string dir = Path.GetDirectoryName(video.FilePath)!;
        string stem = Path.GetFileNameWithoutExtension(video.FilePath);
        return ServeImageFromDirectory(dir, stem);
    }

    /// <summary>
    /// Accepts an uploaded image and saves it next to the video file with the same base name.
    /// Updates ThumbnailUrl to the serve endpoint.
    /// </summary>
    [HttpPost("videos/{videoId:int}/thumbnail")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB
    public async Task<IActionResult> UploadVideoThumbnail(int videoId, IFormFile file)
    {
        try
        {
            var video = await videoRepository.FindOneAsync(videoId);
            if (video is null)
            {
                return NotFound();
            }

            var (canAccess, channel) = await CanAccessChannel(video.ChannelId);
            if (!canAccess)
            {
                return Forbid();
            }

            if (string.IsNullOrEmpty(video.FilePath))
            {
                return BadRequest(new { message = "Please link a file path to this video first." });
            }

            string dir = Path.GetDirectoryName(video.FilePath)!;
            string stem = Path.GetFileNameWithoutExtension(video.FilePath);
            string ext = NormaliseImageExtension(Path.GetExtension(file.FileName));
            string thumbPath = Path.Combine(dir, stem + ext);

            await using (var stream = System.IO.File.Create(thumbPath))
            {
                await file.CopyToAsync(stream);
            }

            video.ThumbnailUrl = $"/api/custom/videos/{videoId}/thumbnail";
            await videoRepository.UpdateAsync(video);

            return Ok(new { thumbnailUrl = video.ThumbnailUrl });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error uploading thumbnail for video {VideoId}", videoId);
            }

            return StatusCode(500, new { message = "An error occurred while saving the thumbnail" });
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Syncs PlaylistVideo junction rows so the video belongs to exactly the given playlists
    /// (limited to playlists owned by <paramref name="channelId"/> to avoid touching platform data).
    /// </summary>
    private async Task SyncVideoPlaylistsAsync(int videoId, int channelId, IReadOnlyList<int> desiredPlaylistIds)
    {
        // Resolve which of the requested IDs actually belong to this channel
        var channelPlaylistIds = (await playlistRepository.FindAsync(new SearchOptions<Playlist>
        {
            Query = x => x.ChannelId == channelId
        }, x => x.Id)).ToHashSet();

        var validDesiredIds = desiredPlaylistIds
            .Where(id => channelPlaylistIds.Contains(id))
            .ToHashSet();

        // Fetch existing junction rows for this video that belong to this channel
        var existing = await playlistVideoRepository.FindAsync(new SearchOptions<PlaylistVideo>
        {
            Query = x => x.VideoId == videoId && channelPlaylistIds.Contains(x.PlaylistId)
        });

        var existingIds = existing.Select(x => x.PlaylistId).ToHashSet();

        // Remove deselected
        var toRemove = existing.Where(x => !validDesiredIds.Contains(x.PlaylistId)).ToList();
        if (toRemove.Count > 0)
        {
            await playlistVideoRepository.DeleteAsync(toRemove);
        }

        // Add newly selected
        foreach (int playlistId in validDesiredIds.Except(existingIds))
        {
            // Place the video at the end of the playlist
            var maxOrder = (await playlistVideoRepository.FindAsync(new SearchOptions<PlaylistVideo>
            {
                Query = x => x.PlaylistId == playlistId
            }, x => (int?)x.Order)).Max() ?? -1;

            await playlistVideoRepository.InsertAsync(new PlaylistVideo
            {
                PlaylistId = playlistId,
                VideoId = videoId,
                Order = maxOrder + 1
            });
        }
    }

    private async Task<bool> CanAccessChannel(int channelId, Channel? channel = null)
    {
        if (userContextService.IsAdministrator())
        {
            return true;
        }

        string? userId = userContextService.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return false;
        }

        var access = await userChannelRepository.FindOneAsync(new SearchOptions<UserChannel>
        {
            Query = x => x.UserId == userId && x.ChannelId == channelId
        });
        return access is not null;
    }

    private async Task<(bool CanAccess, Channel? Channel)> CanAccessChannel(int channelId)
    {
        var channel = await channelRepository.FindOneAsync(channelId);
        bool canAccess = await CanAccessChannel(channelId, null);
        return (canAccess, channel);
    }

    private string GetDownloadPath() =>
        configuration.GetValue<string>("VideoDownload:OutputPath")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

    private string GetPlaylistThumbnailDirectory(Playlist playlist) =>
        Path.Combine(GetDownloadPath(), playlist.Channel.ChannelId, "Playlists");

    private string GetCustomChannelThumbnailDirectory(Channel channel) =>
        Path.Combine(GetDownloadPath(), "_Custom", channel.ChannelId);

    private IActionResult ServeImageFromDirectory(string directory, string stem)
    {
        foreach (string ext in AllowedImageExtensions)
        {
            string path = Path.Combine(directory, stem + ext);
            if (System.IO.File.Exists(path))
            {
                string contentType = ext == ".png"
                    ? "image/png"
                    : ext == ".webp"
                        ? "image/webp"
                        : "image/jpeg";

                return PhysicalFile(path, contentType);
            }
        }
        return NotFound();
    }

    private async Task TryDeletePlaylistThumbnailFileAsync(Playlist playlist)
    {
        try
        {
            if (playlist.ThumbnailUrl?.StartsWith("/api/custom/playlists/") != true)
            {
                return;
            }

            var playlistWithChannel = await playlistRepository.FindOneAsync(new SearchOptions<Playlist>
            {
                Query = x => x.Id == playlist.Id,
                Include = query => query.Include(x => x.Channel)
            });
            if (playlistWithChannel?.Channel is null)
            {
                return;
            }

            string dir = GetPlaylistThumbnailDirectory(playlistWithChannel);
            foreach (string ext in AllowedImageExtensions)
            {
                string path = Path.Combine(dir, playlist.PlaylistId + ext);
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(ex, "Could not delete thumbnail file for playlist {PlaylistId}", playlist.Id);
            }
        }
    }

    private static string NormaliseImageExtension(string ext)
    {
        ext = ext.ToLowerInvariant();
        return AllowedImageExtensions.Contains(ext) ? ext : ".jpg";
    }

    // ── Request records ───────────────────────────────────────────────────────

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
        string? FilePath,
        List<int>? PlaylistIds);
}