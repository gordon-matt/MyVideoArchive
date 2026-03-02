using Hangfire;
using Humanizer;
using MyVideoArchive.Models.Metadata;
using MyVideoArchive.Services.Jobs;
using static System.Net.Mime.MediaTypeNames;

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
    private readonly IConfiguration configuration;
    private readonly IUserContextService userContextService;
    private readonly IBackgroundJobClient backgroundJobClient;
    private readonly VideoMetadataProviderFactory metadataProviderFactory;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<CustomPlaylist> customPlaylistRepository;
    private readonly IRepository<CustomPlaylistVideo> customPlaylistVideoRepository;
    private readonly IRepository<Tag> tagRepository;
    private readonly IRepository<UserChannel> userChannelRepository;
    private readonly IRepository<Video> videoRepository;
    private readonly IRepository<VideoTag> videoTagRepository;

    public CustomPlaylistsApiController(
        ILogger<CustomPlaylistsApiController> logger,
        IConfiguration configuration,
        IUserContextService userContextService,
        IBackgroundJobClient backgroundJobClient,
        VideoMetadataProviderFactory metadataProviderFactory,
        IHttpClientFactory httpClientFactory,
        IRepository<Channel> channelRepository,
        IRepository<CustomPlaylist> customPlaylistRepository,
        IRepository<CustomPlaylistVideo> customPlaylistVideoRepository,
        IRepository<Tag> tagRepository,
        IRepository<UserChannel> userChannelRepository,
        IRepository<Video> videoRepository,
        IRepository<VideoTag> videoTagRepository)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.userContextService = userContextService;
        this.backgroundJobClient = backgroundJobClient;
        this.metadataProviderFactory = metadataProviderFactory;
        this.httpClientFactory = httpClientFactory;
        this.channelRepository = channelRepository;
        this.customPlaylistRepository = customPlaylistRepository;
        this.customPlaylistVideoRepository = customPlaylistVideoRepository;
        this.tagRepository = tagRepository;
        this.userChannelRepository = userChannelRepository;
        this.videoRepository = videoRepository;
        this.videoTagRepository = videoTagRepository;
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
    /// Preview a YouTube playlist: returns its metadata and video list (with in-library status)
    /// without creating anything. Used to populate the selection UI before cloning.
    /// </summary>
    [HttpPost("preview")]
    public async Task<IActionResult> PreviewPlaylist([FromBody] PreviewPlaylistRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Url))
                return BadRequest(new { message = "A playlist URL is required" });

            var provider = metadataProviderFactory.GetProvider(request.Url);
            if (provider is null)
                return BadRequest(new { message = "No metadata provider found for this URL" });

            var playlistMeta = await provider.GetPlaylistMetadataAsync(request.Url, HttpContext.RequestAborted);
            if (playlistMeta is null)
                return BadRequest(new { message = "Could not retrieve playlist metadata. Please check the URL and try again." });

            var videoEntries = await provider.GetPlaylistVideosAsync(request.Url, HttpContext.RequestAborted);
            if (videoEntries.Count == 0)
                return BadRequest(new { message = "The playlist appears to be empty or could not be read." });

            // Check which videos are already in the library
            var videoIds = videoEntries
                .Where(v => !string.IsNullOrEmpty(v.VideoId))
                .Select(v => v.VideoId)
                .ToList();

            var existingVideos = await videoRepository.FindAsync(
                new SearchOptions<Video>
                {
                    Query = x => videoIds.Contains(x.VideoId) && x.Platform == playlistMeta.Platform
                },
                x => x.VideoId);

            var inLibrarySet = existingVideos.ToHashSet();

            var videos = videoEntries
                .Where(v => !string.IsNullOrEmpty(v.VideoId))
                .Select(v => new
                {
                    v.VideoId,
                    v.Title,
                    v.ThumbnailUrl,
                    DurationSeconds = v.Duration.HasValue ? (int?)v.Duration.Value.TotalSeconds : null,
                    v.ChannelName,
                    v.Url,
                    IsInLibrary = inLibrarySet.Contains(v.VideoId)
                })
                .ToList();

            return Ok(new
            {
                name = playlistMeta.Name,
                description = playlistMeta.Description,
                thumbnailUrl = playlistMeta.ThumbnailUrl,
                platform = playlistMeta.Platform,
                videos
            });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(ex, "Error previewing playlist from URL {Url}", request.Url);

            return StatusCode(500, new { message = "An error occurred while fetching the playlist" });
        }
    }

    /// <summary>
    /// Clone a YouTube playlist: creates video records for the selected videos,
    /// queues downloads for any not already in the library, downloads the playlist thumbnail,
    /// and returns the new custom playlist.
    /// </summary>
    [HttpPost("clone")]
    public async Task<IActionResult> ClonePlaylist([FromBody] ClonePlaylistRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Url))
                return BadRequest(new { message = "A playlist URL is required" });

            if (request.SelectedVideoIds.Count == 0)
                return BadRequest(new { message = "Please select at least one video to clone" });

            var provider = metadataProviderFactory.GetProvider(request.Url);
            if (provider is null)
                return BadRequest(new { message = "No metadata provider found for this URL" });

            var playlistMeta = await provider.GetPlaylistMetadataAsync(request.Url, HttpContext.RequestAborted);
            if (playlistMeta is null)
                return BadRequest(new { message = "Could not retrieve playlist metadata. Please check the URL and try again." });

            var videoEntries = await provider.GetPlaylistVideosAsync(request.Url, HttpContext.RequestAborted);

            // Filter to only the user-selected videos, preserving playlist order
            var selectedSet = request.SelectedVideoIds.ToHashSet();
            var selectedEntries = videoEntries
                .Where(v => !string.IsNullOrEmpty(v.VideoId) && selectedSet.Contains(v.VideoId))
                .ToList();

            if (selectedEntries.Count == 0)
                return BadRequest(new { message = "None of the selected videos could be found in the playlist." });

            // Create the custom playlist
            var playlist = new CustomPlaylist
            {
                UserId = userId,
                Name = playlistMeta.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(playlistMeta.Description) ? null : playlistMeta.Description.Trim(),
                CreatedAt = DateTime.UtcNow
            };
            await customPlaylistRepository.InsertAsync(playlist);

            // Download and store the playlist thumbnail
            if (!string.IsNullOrEmpty(playlistMeta.ThumbnailUrl))
            {
                try
                {
                    using var http = httpClientFactory.CreateClient();
                    var imageBytes = await http.GetByteArrayAsync(playlistMeta.ThumbnailUrl, HttpContext.RequestAborted);
                    string uploadDir = GetCustomPlaylistsThumbnailDirectory();
                    Directory.CreateDirectory(uploadDir);
                    string thumbPath = Path.Combine(uploadDir, $"{playlist.Id}-thumbnail.jpg");
                    await System.IO.File.WriteAllBytesAsync(thumbPath, imageBytes, HttpContext.RequestAborted);
                    playlist.ThumbnailUrl = $"/api/custom-playlists/{playlist.Id}/thumbnail";
                    await customPlaylistRepository.UpdateAsync(playlist);
                }
                catch (Exception ex)
                {
                    if (logger.IsEnabled(LogLevel.Warning))
                        logger.LogWarning(ex, "Failed to download thumbnail for cloned playlist {PlaylistId}", playlist.Id);
                }
            }

            int newVideoCount = 0;
            int alreadyInLibraryCount = 0;
            int order = 0;

            foreach (var videoMeta in selectedEntries)
            {
                // Find or create the channel
                string channelPlatformId = videoMeta.ChannelId ?? videoMeta.ChannelName ?? "unknown";
                var channel = await channelRepository.FindOneAsync(new SearchOptions<Channel>
                {
                    Query = x => x.ChannelId == channelPlatformId && x.Platform == videoMeta.Platform
                });

                if (channel is null)
                {
                    // Fetch channel metadata to create a proper channel record
                    ChannelMetadata? channelMeta = null;
                    if (!string.IsNullOrEmpty(videoMeta.ChannelId))
                    {
                        var channelUrl = $"https://www.youtube.com/channel/{videoMeta.ChannelId}";
                        channelMeta = await provider.GetChannelMetadataAsync(channelUrl, HttpContext.RequestAborted);
                    }

                    channel = new Channel
                    {
                        ChannelId = channelPlatformId,
                        Name = videoMeta.ChannelName ?? "Unknown Channel",
                        Url = string.IsNullOrEmpty(videoMeta.ChannelId)
                            ? string.Empty
                            : $"https://www.youtube.com/channel/{videoMeta.ChannelId}",
                        //Description = channelMeta?.Description,
                        //ThumbnailUrl = channelMeta?.ThumbnailUrl,
                        //SubscriberCount = channelMeta?.SubscriberCount,
                        Platform = videoMeta.Platform,
                        SubscribedAt = DateTime.UtcNow
                    };
                    await channelRepository.InsertAsync(channel);
                }

                // Find or create the video
                var video = await videoRepository.FindOneAsync(new SearchOptions<Video>
                {
                    Query = x => x.VideoId == videoMeta.VideoId && x.Platform == videoMeta.Platform
                });

                if (video is null)
                {
                    video = new Video
                    {
                        VideoId = videoMeta.VideoId,
                        Title = videoMeta.Title,
                        Description = videoMeta.Description,
                        Url = videoMeta.Url,
                        ThumbnailUrl = videoMeta.ThumbnailUrl,
                        Platform = videoMeta.Platform,
                        Duration = videoMeta.Duration,
                        UploadDate = videoMeta.UploadDate,
                        ViewCount = videoMeta.ViewCount,
                        LikeCount = videoMeta.LikeCount,
                        ChannelId = channel.Id,
                        IsQueued = true
                    };
                    await videoRepository.InsertAsync(video);
                    backgroundJobClient.Enqueue<VideoDownloadJob>(job => job.ExecuteAsync(video.Id, CancellationToken.None));
                    newVideoCount++;
                }
                else
                {
                    alreadyInLibraryCount++;
                }

                bool tagIt = !await userChannelRepository.ExistsAsync(x => x.Channel.ChannelId == channelPlatformId && x.UserId == userId);
                if (tagIt)
                {
                    // Get or create "standalone" tag for this user
                    var standaloneTag = await GetOrCreateTagAsync(userId, Constants.StandaloneTag);

                    // Tag the video as standalone if not already tagged
                    var alreadyTagged = await videoTagRepository.ExistsAsync(x => x.VideoId == video.Id && x.TagId == standaloneTag.Id);
                    if (!alreadyTagged)
                    {
                        await videoTagRepository.InsertAsync(new VideoTag
                        {
                            VideoId = video.Id,
                            TagId = standaloneTag.Id
                        });
                    }
                }

                bool alreadyInPlaylist = await customPlaylistVideoRepository.ExistsAsync(
                    x => x.CustomPlaylistId == playlist.Id && x.VideoId == video.Id);

                if (!alreadyInPlaylist)
                {
                    await customPlaylistVideoRepository.InsertAsync(new CustomPlaylistVideo
                    {
                        CustomPlaylistId = playlist.Id,
                        VideoId = video.Id,
                        Order = order++
                    });
                }
            }

            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation(
                    "Cloned playlist '{Name}' with {Total} videos ({New} new, {Existing} already in library)",
                    playlist.Name, order, newVideoCount, alreadyInLibraryCount);

            return Ok(new
            {
                id = playlist.Id,
                name = playlist.Name,
                totalVideos = order,
                newVideos = newVideoCount,
                alreadyInLibrary = alreadyInLibraryCount
            });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(ex, "Error cloning playlist from URL {Url}", request.Url);

            return StatusCode(500, new { message = "An error occurred while cloning the playlist" });
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

    /// <summary>Serves the locally stored thumbnail for a custom playist.</summary>
    [HttpGet("{id}/thumbnail")]
    public async Task<IActionResult> GetPlaylistThumbnail(int id)
    {
        var playlist = await customPlaylistRepository.FindOneAsync(id);
        if (playlist is null)
        {
            return NotFound();
        }

        return ServeImageFromDirectory(id);
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
            string uploadDir = GetCustomPlaylistsThumbnailDirectory();
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

            string thumbPath = Path.Combine(uploadDir, $"{id}-thumbnail{ext}");
            await using (var stream = System.IO.File.Create(thumbPath))
            {
                await file.CopyToAsync(stream);
            }

            playlist.ThumbnailUrl = $"/api/custom-playlists/{id}/thumbnail";
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

    private static string NormaliseImageExtension(string ext) =>
        ext.ToLowerInvariant() switch
        {
            ".jpeg" => ".jpg",
            ".jpg" or ".png" or ".webp" => ext.ToLowerInvariant(),
            _ => ".jpg"
        };

    private string GetCustomPlaylistsThumbnailDirectory() =>
        Path.Combine(GetDownloadPath(), "_CustomPlaylists");

    private string GetDownloadPath() => configuration.GetValue<string>("VideoDownload:OutputPath")
        ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

    private IActionResult ServeImageFromDirectory(int playlistId)
    {
        string uploadDir = GetCustomPlaylistsThumbnailDirectory();

        foreach (string ext in AllowedImageExtensions)
        {
            string thumbPath = Path.Combine(uploadDir, $"{playlistId}-thumbnail{ext}");
            if (System.IO.File.Exists(thumbPath))
            {
                string contentType = ext == ".png"
                    ? "image/png"
                    : ext == ".webp"
                        ? "image/webp"
                        : "image/jpeg";

                return PhysicalFile(thumbPath, contentType);
            }
        }

        return NotFound();
    }

    private async Task<bool> UserOwnsPlaylist(string userId, int playlistId) =>
                                                                await customPlaylistRepository.ExistsAsync(x => x.Id == playlistId && x.UserId == userId);

    private async Task<Tag> GetOrCreateTagAsync(string userId, string name)
    {
        var existing = await tagRepository.FindOneAsync(new SearchOptions<Tag>
        {
            Query = x => x.UserId == userId && x.Name.ToLower() == name.ToLower()
        });

        if (existing is not null) return existing;

        var tag = new Tag { UserId = userId, Name = name };
        await tagRepository.InsertAsync(tag);
        return tag;
    }
}

public class CreateCustomPlaylistRequest
{
    public string? Description { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class PreviewPlaylistRequest
{
    public string Url { get; set; } = string.Empty;
}

public class ClonePlaylistRequest
{
    public string Url { get; set; } = string.Empty;
    public List<string> SelectedVideoIds { get; set; } = [];
}