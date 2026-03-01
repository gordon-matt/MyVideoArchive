namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for managing user custom playlists
/// </summary>
[Authorize]
[ApiController]
[Route("api/custom-playlists")]
public class CustomPlaylistsApiController : ControllerBase
{
    private static readonly string[] AllowedImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    private readonly ILogger<CustomPlaylistsApiController> logger;
    private readonly IWebHostEnvironment webHostEnvironment;
    private readonly IUserContextService userContextService;
    private readonly IRepository<CustomPlaylist> customPlaylistRepository;
    private readonly IRepository<CustomPlaylistVideo> customPlaylistVideoRepository;
    private readonly IRepository<Video> videoRepository;

    public CustomPlaylistsApiController(
        ILogger<CustomPlaylistsApiController> logger,
        IWebHostEnvironment webHostEnvironment,
        IUserContextService userContextService,
        IRepository<CustomPlaylist> customPlaylistRepository,
        IRepository<CustomPlaylistVideo> customPlaylistVideoRepository,
        IRepository<Video> videoRepository)
    {
        this.logger = logger;
        this.webHostEnvironment = webHostEnvironment;
        this.userContextService = userContextService;
        this.customPlaylistRepository = customPlaylistRepository;
        this.customPlaylistVideoRepository = customPlaylistVideoRepository;
        this.videoRepository = videoRepository;
    }

    private async Task<bool> UserOwnsPlaylist(string userId, int playlistId) =>
        await customPlaylistRepository.ExistsAsync(x => x.Id == playlistId && x.UserId == userId);

    /// <summary>
    /// Get all custom playlists for the current user
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetPlaylists(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 60)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var pagedPlaylists = await customPlaylistRepository.FindAsync(
                new SearchOptions<CustomPlaylist>
                {
                    Query = x => x.UserId == userId,
                    OrderBy = q => q.OrderByDescending(x => x.CreatedAt),
                    PageNumber = page,
                    PageSize = pageSize,
                    Include = q => q.Include(x => x.CustomPlaylistVideos)
                });

            var playlists = pagedPlaylists.Select(x => new
            {
                x.Id,
                x.Name,
                x.Description,
                x.ThumbnailUrl,
                x.CreatedAt,
                VideoCount = x.CustomPlaylistVideos.Count
            }).ToList();

            return Ok(new
            {
                playlists,
                pagination = new
                {
                    currentPage = page,
                    pageSize,
                    totalCount = pagedPlaylists.ItemCount,
                    totalPages = pagedPlaylists.PageCount
                }
            });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving custom playlists");
            }

            return StatusCode(500, new { message = "An error occurred while retrieving playlists" });
        }
    }

    /// <summary>
    /// Create a new custom playlist
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreatePlaylist([FromBody] CreateCustomPlaylistRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { message = "Playlist name is required" });
            }

            var playlist = new CustomPlaylist
            {
                UserId = userId,
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            await customPlaylistRepository.InsertAsync(playlist);

            return Ok(new { id = playlist.Id, name = playlist.Name });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error creating custom playlist");
            }

            return StatusCode(500, new { message = "An error occurred while creating the playlist" });
        }
    }

    /// <summary>
    /// Update a custom playlist
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePlaylist(int id, [FromBody] CreateCustomPlaylistRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var playlist = await customPlaylistRepository.FindOneAsync(new SearchOptions<CustomPlaylist>
            {
                Query = x => x.Id == id && x.UserId == userId
            });

            if (playlist is null)
            {
                return NotFound();
            }

            playlist.Name = request.Name?.Trim() ?? playlist.Name;
            playlist.Description = request.Description?.Trim();
            await customPlaylistRepository.UpdateAsync(playlist);

            return Ok(new { id = playlist.Id, name = playlist.Name });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error updating custom playlist {PlaylistId}", id);
            }

            return StatusCode(500, new { message = "An error occurred while updating the playlist" });
        }
    }

    /// <summary>
    /// Delete a custom playlist
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePlaylist(int id)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var playlist = await customPlaylistRepository.FindOneAsync(new SearchOptions<CustomPlaylist>
            {
                Query = x => x.Id == id && x.UserId == userId
            });

            if (playlist is null)
            {
                return NotFound();
            }

            await customPlaylistRepository.DeleteAsync(playlist);
            return NoContent();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error deleting custom playlist {PlaylistId}", id);
            }

            return StatusCode(500, new { message = "An error occurred while deleting the playlist" });
        }
    }

    /// <summary>
    /// Get videos in a custom playlist
    /// </summary>
    [HttpGet("{id}/videos")]
    public async Task<IActionResult> GetPlaylistVideos(
        int id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 60)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            if (!await UserOwnsPlaylist(userId, id))
            {
                return NotFound();
            }

            var playlist = await customPlaylistRepository.FindOneAsync(id);
            if (playlist is null)
            {
                return NotFound();
            }

            var pagedPlaylistVideos = await customPlaylistVideoRepository.FindAsync(
                new SearchOptions<CustomPlaylistVideo>
                {
                    Query = x => x.CustomPlaylistId == id,
                    OrderBy = q => q.OrderBy(x => x.Order),
                    PageNumber = page,
                    PageSize = pageSize,
                    Include = q => q.Include(x => x.Video).ThenInclude(v => v.Channel)
                });

            var videos = pagedPlaylistVideos.Select(x => new
            {
                x.Order,
                Video = new
                {
                    x.Video.Id,
                    x.Video.Title,
                    x.Video.ThumbnailUrl,
                    x.Video.Duration,
                    x.Video.DownloadedAt,
                    x.Video.Platform,
                    Channel = new { x.Video.Channel.Id, x.Video.Channel.Name }
                }
            }).ToList();

            return Ok(new
            {
                playlist = new { playlist.Id, playlist.Name, playlist.Description, playlist.ThumbnailUrl },
                videos,
                pagination = new
                {
                    currentPage = page,
                    pageSize,
                    totalCount = pagedPlaylistVideos.ItemCount,
                    totalPages = pagedPlaylistVideos.PageCount
                }
            });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving videos for custom playlist {PlaylistId}", id);
            }

            return StatusCode(500, new { message = "An error occurred while retrieving playlist videos" });
        }
    }

    /// <summary>
    /// Add a video to a custom playlist
    /// </summary>
    [HttpPost("{id}/videos/{videoId}")]
    public async Task<IActionResult> AddVideoToPlaylist(int id, int videoId)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            if (!await UserOwnsPlaylist(userId, id))
            {
                return NotFound(new { message = "Playlist not found" });
            }

            bool videoExists = await videoRepository.ExistsAsync(x => x.Id == videoId);
            if (!videoExists)
            {
                return NotFound(new { message = "Video not found" });
            }

            bool alreadyInPlaylist = await customPlaylistVideoRepository.ExistsAsync(
                x => x.CustomPlaylistId == id && x.VideoId == videoId);

            if (alreadyInPlaylist)
            {
                return Ok(new { message = "Video is already in this playlist" });
            }

            // Get the next order value
            var existingOrders = (await customPlaylistVideoRepository.FindAsync(
                new SearchOptions<CustomPlaylistVideo>
                {
                    Query = x => x.CustomPlaylistId == id
                },
                x => (int?)x.Order)).ToList();
            int maxOrder = existingOrders.Count > 0 ? existingOrders.Max() ?? -1 : -1;

            await customPlaylistVideoRepository.InsertAsync(new CustomPlaylistVideo
            {
                CustomPlaylistId = id,
                VideoId = videoId,
                Order = maxOrder + 1
            });

            return Ok(new { message = "Video added to playlist" });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error adding video {VideoId} to playlist {PlaylistId}", videoId, id);
            }

            return StatusCode(500, new { message = "An error occurred while adding the video" });
        }
    }

    /// <summary>
    /// Remove a video from a custom playlist
    /// </summary>
    [HttpDelete("{id}/videos/{videoId}")]
    public async Task<IActionResult> RemoveVideoFromPlaylist(int id, int videoId)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            if (!await UserOwnsPlaylist(userId, id))
            {
                return NotFound(new { message = "Playlist not found" });
            }

            var entry = await customPlaylistVideoRepository.FindOneAsync(new SearchOptions<CustomPlaylistVideo>
            {
                Query = x => x.CustomPlaylistId == id && x.VideoId == videoId
            });

            if (entry is null)
            {
                return NotFound(new { message = "Video not found in playlist" });
            }

            await customPlaylistVideoRepository.DeleteAsync(entry);
            return NoContent();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error removing video {VideoId} from playlist {PlaylistId}", videoId, id);
            }

            return StatusCode(500, new { message = "An error occurred while removing the video" });
        }
    }

    /// <summary>
    /// Upload a thumbnail image for a custom playlist
    /// </summary>
    [HttpPost("{id}/thumbnail")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadThumbnail(int id, IFormFile file)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var playlist = await customPlaylistRepository.FindOneAsync(new SearchOptions<CustomPlaylist>
            {
                Query = x => x.Id == id && x.UserId == userId
            });

            if (playlist is null)
            {
                return NotFound();
            }

            string ext = NormaliseImageExtension(Path.GetExtension(file.FileName));
            string uploadDir = Path.Combine(webHostEnvironment.WebRootPath, "uploads", "custom-playlists", id.ToString());
            Directory.CreateDirectory(uploadDir);

            // Remove any previous thumbnail with another extension
            foreach (string other in AllowedImageExtensions)
            {
                string otherPath = Path.Combine(uploadDir, "thumbnail" + other);
                if (System.IO.File.Exists(otherPath))
                {
                    System.IO.File.Delete(otherPath);
                }
            }

            string thumbPath = Path.Combine(uploadDir, "thumbnail" + ext);
            await using (var stream = System.IO.File.Create(thumbPath))
            {
                await file.CopyToAsync(stream);
            }

            playlist.ThumbnailUrl = $"/uploads/custom-playlists/{id}/thumbnail{ext}";
            await customPlaylistRepository.UpdateAsync(playlist);

            return Ok(new { thumbnailUrl = playlist.ThumbnailUrl });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error uploading thumbnail for custom playlist {PlaylistId}", id);
            }

            return StatusCode(500, new { message = "An error occurred while saving the thumbnail" });
        }
    }

    /// <summary>
    /// Get all custom playlists for the current user that contain a specific video
    /// </summary>
    [HttpGet("for-video/{videoId:int}")]
    public async Task<IActionResult> GetPlaylistsForVideo(int videoId)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var memberships = await customPlaylistVideoRepository.FindAsync(
                new SearchOptions<CustomPlaylistVideo>
                {
                    Query = x => x.VideoId == videoId && x.CustomPlaylist.UserId == userId,
                    Include = q => q.Include(x => x.CustomPlaylist)
                });

            var playlists = memberships
                .Select(x => new { x.CustomPlaylist.Id, x.CustomPlaylist.Name })
                .ToList();

            return Ok(new { playlists });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving playlists for video {VideoId}", videoId);
            }

            return StatusCode(500, new { message = "An error occurred while retrieving playlists for video" });
        }
    }

    private static string NormaliseImageExtension(string ext) =>
        ext.ToLowerInvariant() switch
        {
            ".jpeg" => ".jpg",
            ".jpg" or ".png" or ".webp" => ext.ToLowerInvariant(),
            _ => ".jpg"
        };
}

public class CreateCustomPlaylistRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}