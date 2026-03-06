using MyVideoArchive.Models.Metadata;
using MyVideoArchive.Services.Content.Providers;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// Manages channel banner and avatar images – including fetching available thumbnails
/// from the source platform and accepting user-supplied uploads.
/// </summary>
[ApiController]
[Route("api/channels")]
[Authorize]
public class ChannelImagesApiController : ControllerBase
{
    private static readonly string[] AllowedImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    private readonly ILogger<ChannelImagesApiController> logger;
    private readonly IConfiguration configuration;
    private readonly IUserContextService userContextService;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<UserChannel> userChannelRepository;
    private readonly VideoMetadataProviderFactory metadataProviderFactory;

    public ChannelImagesApiController(
        ILogger<ChannelImagesApiController> logger,
        IConfiguration configuration,
        IUserContextService userContextService,
        IRepository<Channel> channelRepository,
        IRepository<UserChannel> userChannelRepository,
        VideoMetadataProviderFactory metadataProviderFactory)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.userContextService = userContextService;
        this.channelRepository = channelRepository;
        this.userChannelRepository = userChannelRepository;
        this.metadataProviderFactory = metadataProviderFactory;
    }

    /// <summary>
    /// Fetches available thumbnails for a channel URL without creating a channel.
    /// Used during the "Add Channel" flow so the user can pick banner/avatar before subscribing.
    /// </summary>
    [HttpGet("images/preview")]
    public async Task<IActionResult> PreviewChannelImages(
        [FromQuery] string url,
        [FromQuery] string platform,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(platform))
        {
            return BadRequest(new { message = "url and platform are required." });
        }

        var provider = metadataProviderFactory.GetProviderByPlatform(platform);
        if (provider is null)
        {
            return BadRequest(new { message = $"No metadata provider found for platform '{platform}'." });
        }

        try
        {
            var metadata = await provider.GetChannelMetadataAsync(url, cancellationToken);
            if (metadata is null)
            {
                return BadRequest(new { message = "Unable to fetch channel metadata. Check the URL and try again." });
            }

            return Ok(new
            {
                channelId = metadata.ChannelId,
                name = metadata.Name,
                description = metadata.Description,
                subscriberCount = metadata.SubscriberCount,
                defaultBannerUrl = metadata.BannerUrl,
                thumbnails = metadata.Thumbnails
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error previewing channel images for {Url}", url);
            return StatusCode(500, new { message = "An error occurred while fetching channel images." });
        }
    }

    /// <summary>
    /// Returns available thumbnails for an existing channel by re-fetching from the platform.
    /// Used to populate the thumbnail picker when editing an existing channel's images.
    /// </summary>
    [HttpGet("{id:int}/images/available")]
    public async Task<IActionResult> GetAvailableImages(int id, CancellationToken cancellationToken = default)
    {
        var (canAccess, channel) = await CanAccessChannelAsync(id);
        if (!canAccess) return Forbid();
        if (channel is null) return NotFound();

        if (channel.Platform == "Custom")
        {
            return Ok(new { thumbnails = Array.Empty<ThumbnailInfo>(), defaultBannerUrl = (string?)null });
        }

        var provider = metadataProviderFactory.GetProviderByPlatform(channel.Platform);
        if (provider is null)
        {
            return Ok(new { thumbnails = Array.Empty<ThumbnailInfo>(), defaultBannerUrl = (string?)null });
        }

        try
        {
            var metadata = await provider.GetChannelMetadataAsync(channel.Url, cancellationToken);
            return Ok(new
            {
                thumbnails = metadata?.Thumbnails ?? [],
                defaultBannerUrl = metadata?.BannerUrl
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching available images for channel {ChannelId}", id);
            return StatusCode(500, new { message = "An error occurred while fetching channel images." });
        }
    }

    /// <summary>
    /// Updates the banner and/or avatar URL for a channel (selects from platform-provided thumbnails).
    /// </summary>
    [HttpPut("{id:int}/images")]
    public async Task<IActionResult> UpdateChannelImages(
        int id,
        [FromBody] UpdateChannelImagesRequest request)
    {
        var (canAccess, channel) = await CanAccessChannelAsync(id);
        if (!canAccess) return Forbid();
        if (channel is null) return NotFound();

        if (request.BannerUrl is not null)
        {
            channel.BannerUrl = string.IsNullOrEmpty(request.BannerUrl) ? null : request.BannerUrl;
        }

        if (request.AvatarUrl is not null)
        {
            channel.AvatarUrl = string.IsNullOrEmpty(request.AvatarUrl) ? null : request.AvatarUrl;
        }

        await channelRepository.UpdateAsync(channel);
        return Ok(new { bannerUrl = channel.BannerUrl, avatarUrl = channel.AvatarUrl });
    }

    /// <summary>
    /// Uploads a custom banner image for a channel.
    /// </summary>
    [HttpPost("{id:int}/banner/upload")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadBanner(int id, IFormFile file)
    {
        var (canAccess, channel) = await CanAccessChannelAsync(id);
        if (!canAccess) return Forbid();
        if (channel is null) return NotFound();

        var (url, errorMessage) = await SaveChannelImageAsync(id, channel, file, "banner");
        if (errorMessage is not null) return BadRequest(new { message = errorMessage });

        channel.BannerUrl = url;
        await channelRepository.UpdateAsync(channel);
        return Ok(new { bannerUrl = url });
    }

    /// <summary>
    /// Uploads a custom avatar image for a channel.
    /// </summary>
    [HttpPost("{id:int}/avatar/upload")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadAvatar(int id, IFormFile file)
    {
        var (canAccess, channel) = await CanAccessChannelAsync(id);
        if (!canAccess) return Forbid();
        if (channel is null) return NotFound();

        var (url, errorMessage) = await SaveChannelImageAsync(id, channel, file, "avatar");
        if (errorMessage is not null) return BadRequest(new { message = errorMessage });

        channel.AvatarUrl = url;
        await channelRepository.UpdateAsync(channel);
        return Ok(new { avatarUrl = url });
    }

    /// <summary>
    /// Serves an uploaded banner image for a channel.
    /// </summary>
    [HttpGet("{id:int}/banner")]
    public async Task<IActionResult> GetBanner(int id)
    {
        var (canAccess, channel) = await CanAccessChannelAsync(id);
        if (!canAccess) return Forbid();
        if (channel is null) return NotFound();

        var fileInfo = ResolveUploadedImage(id, channel, "banner");
        if (fileInfo is null) return NotFound();
        return PhysicalFile(fileInfo.Value.FilePath, fileInfo.Value.ContentType);
    }

    /// <summary>
    /// Serves an uploaded avatar image for a channel.
    /// </summary>
    [HttpGet("{id:int}/avatar")]
    public async Task<IActionResult> GetAvatar(int id)
    {
        var (canAccess, channel) = await CanAccessChannelAsync(id);
        if (!canAccess) return Forbid();
        if (channel is null) return NotFound();

        var fileInfo = ResolveUploadedImage(id, channel, "avatar");
        if (fileInfo is null) return NotFound();
        return PhysicalFile(fileInfo.Value.FilePath, fileInfo.Value.ContentType);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<(bool CanAccess, Channel? Channel)> CanAccessChannelAsync(int channelId)
    {
        var channel = await channelRepository.FindOneAsync(channelId);
        if (channel is null) return (true, null); // caller should return 404

        if (userContextService.IsAdministrator()) return (true, channel);

        string? userId = userContextService.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId)) return (false, channel);

        bool hasAccess = await userChannelRepository.ExistsAsync(
            x => x.UserId == userId && x.ChannelId == channelId);

        return (hasAccess, channel);
    }

    private async Task<(string? Url, string? Error)> SaveChannelImageAsync(
        int channelDbId,
        Channel channel,
        IFormFile file,
        string role)
    {
        if (file is null || file.Length == 0)
            return (null, "No file provided.");

        string ext = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(ext))
            return (null, $"Invalid file type. Allowed: {string.Join(", ", AllowedImageExtensions)}");

        string dir = GetChannelImageDirectory(channelDbId, channel);
        System.IO.Directory.CreateDirectory(dir);

        // Delete other-extension variants of the same role
        foreach (string otherExt in AllowedImageExtensions)
        {
            string otherPath = System.IO.Path.Combine(dir, role + otherExt);
            if (System.IO.File.Exists(otherPath)) System.IO.File.Delete(otherPath);
        }

        string savePath = System.IO.Path.Combine(dir, role + ext);
        await using var stream = file.OpenReadStream();
        await using var fs = System.IO.File.Create(savePath);
        await stream.CopyToAsync(fs);

        return ($"/api/channels/{channelDbId}/{role}", null);
    }

    private (string FilePath, string ContentType)? ResolveUploadedImage(int channelDbId, Channel channel, string role)
    {
        string dir = GetChannelImageDirectory(channelDbId, channel);
        foreach (string ext in AllowedImageExtensions)
        {
            string filePath = System.IO.Path.Combine(dir, role + ext);
            if (System.IO.File.Exists(filePath))
            {
                string contentType = ext is ".png" ? "image/png" : ext is ".webp" ? "image/webp" : "image/jpeg";
                return (filePath, contentType);
            }
        }
        return null;
    }

    private string GetChannelImageDirectory(int channelDbId, Channel channel)
    {
        string downloadPath = configuration.GetValue<string>("VideoDownload:OutputPath")
            ?? System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Downloads");

        // Platform channels use their channelId as folder name; all store images under _images/
        string folderName = channel.Platform == "Custom" ? $"_Custom/{channel.ChannelId}" : channel.ChannelId;
        return System.IO.Path.Combine(downloadPath, folderName, "_images");
    }
}

public record UpdateChannelImagesRequest(string? BannerUrl, string? AvatarUrl);
