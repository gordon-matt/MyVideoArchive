using Hangfire;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// API controller for channel operations like sync and admin deletion
/// </summary>
[Authorize]
[ApiController]
[Route("api/channels")]
public class ChannelOperationsApiController : ControllerBase
{
    private readonly ILogger<ChannelOperationsApiController> logger;
    private readonly IConfiguration configuration;
    private readonly IBackgroundJobClient backgroundJobClient;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<CustomPlaylistVideo> customPlaylistVideoRepository;
    private readonly IRepository<Playlist> playlistRepository;
    private readonly IRepository<PlaylistVideo> playlistVideoRepository;
    private readonly IRepository<UserChannel> userChannelRepository;
    private readonly IRepository<UserPlaylist> userPlaylistRepository;
    private readonly IRepository<UserPlaylistVideo> userPlaylistVideoRepository;
    private readonly IRepository<UserVideo> userVideoRepository;
    private readonly IRepository<Video> videoRepository;
    private readonly IRepository<VideoTag> videoTagRepository;

    public ChannelOperationsApiController(
        ILogger<ChannelOperationsApiController> logger,
        IConfiguration configuration,
        IBackgroundJobClient backgroundJobClient,
        IRepository<Channel> channelRepository,
        IRepository<CustomPlaylistVideo> customPlaylistVideoRepository,
        IRepository<Playlist> playlistRepository,
        IRepository<PlaylistVideo> playlistVideoRepository,
        IRepository<UserChannel> userChannelRepository,
        IRepository<UserPlaylist> userPlaylistRepository,
        IRepository<UserPlaylistVideo> userPlaylistVideoRepository,
        IRepository<UserVideo> userVideoRepository,
        IRepository<Video> videoRepository,
        IRepository<VideoTag> videoTagRepository)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.backgroundJobClient = backgroundJobClient;
        this.channelRepository = channelRepository;
        this.customPlaylistVideoRepository = customPlaylistVideoRepository;
        this.playlistRepository = playlistRepository;
        this.playlistVideoRepository = playlistVideoRepository;
        this.userChannelRepository = userChannelRepository;
        this.userPlaylistRepository = userPlaylistRepository;
        this.userPlaylistVideoRepository = userPlaylistVideoRepository;
        this.userVideoRepository = userVideoRepository;
        this.videoRepository = videoRepository;
        this.videoTagRepository = videoTagRepository;
    }

    /// <summary>
    /// Trigger sync for all channels
    /// </summary>
    [HttpPost("sync-all")]
    public IActionResult SyncAllChannels()
    {
        try
        {
            backgroundJobClient.Enqueue<ChannelSyncJob>(job =>
                job.SyncAllChannelsAsync(CancellationToken.None));

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Queued sync job for all channels");
            }

            return Ok(new
            {
                message = "Sync job queued successfully for all channels"
            });
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error queueing sync job for all channels");
            }

            return StatusCode(500, new { message = "An error occurred while queueing sync job" });
        }
    }

    /// <summary>
    /// Admin-only: unsubscribe all users from a channel, with optional metadata and file deletion.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = Constants.Roles.Administrator)]
    public async Task<IActionResult> AdminDeleteChannel(
        int id,
        [FromQuery] bool deleteMetadata = false,
        [FromQuery] bool deleteFiles = false)
    {
        try
        {
            var channel = await channelRepository.FindOneAsync(id);
            if (channel is null)
            {
                return NotFound(new { message = "Channel not found" });
            }

            // Always: remove all user subscriptions & related playlist/video associations for the channel
            await customPlaylistVideoRepository.DeleteAsync(x => x.Video.ChannelId == id);
            await userPlaylistVideoRepository.DeleteAsync(x => x.Video.ChannelId == id);
            await userPlaylistRepository.DeleteAsync(x => x.Playlist.ChannelId == id);
            await userVideoRepository.DeleteAsync(x => x.Video.ChannelId == id);

            int subscriptionCount = await userChannelRepository.DeleteAsync(x => x.ChannelId == id);

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Admin removed {Count} subscription(s) from channel {ChannelId}",
                    subscriptionCount, id);
            }

            if (deleteFiles)
            {
                int deletedFileCount = 0;

                string downloadPath = configuration.GetValue<string>("VideoDownload:OutputPath")
                    ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

                string channelPath = Path.Combine(downloadPath, channel.ChannelId);

                if (!string.IsNullOrEmpty(channelPath) && Directory.Exists(channelPath))
                {
                    string[] files = Directory.GetFiles(channelPath);

                    foreach (string file in files)
                    {
                        System.IO.File.SetAttributes(file, FileAttributes.Normal); // handle read-only files
                        System.IO.File.Delete(file);
                        deletedFileCount++;
                    }

                    Directory.Delete(channelPath);
                }

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Admin deleted {Count} video file(s) for channel {ChannelId}",
                        deletedFileCount, id);
                }

                if (!deleteMetadata)
                {
                    await videoRepository.UpdateAsync(
                        x => x.ChannelId == id && x.FilePath != null,
                        setters => setters
                            .SetProperty(p => p.FilePath, (string?)null)
                            .SetProperty(p => p.FileSize, (long?)null)
                            .SetProperty(p => p.DownloadedAt, (DateTime?)null));
                }
            }

            if (deleteMetadata)
            {
                await videoTagRepository.DeleteAsync(x => x.Video.ChannelId == id);
                await playlistVideoRepository.DeleteAsync(x => x.Video.ChannelId == id);
                await videoRepository.DeleteAsync(x => x.ChannelId == id);
                await playlistRepository.DeleteAsync(x => x.ChannelId == id);
                await channelRepository.DeleteAsync(channel);

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Admin deleted channel {ChannelId} and all associated metadata", id);
                }
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error performing admin delete for channel {ChannelId}", id);
            }

            return StatusCode(500, new { message = "An error occurred while deleting the channel" });
        }
    }
}