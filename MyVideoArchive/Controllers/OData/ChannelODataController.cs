using Hangfire;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using MyVideoArchive.Models.Metadata;

namespace MyVideoArchive.Controllers.OData;

[Authorize]
public class ChannelODataController : ODataController
{
    private readonly ILogger<ChannelODataController> logger;
    private readonly IUserContextService userContextService;
    private readonly VideoMetadataProviderFactory metadataProviderFactory;
    private readonly IBackgroundJobClient backgroundJobClient;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<Tag> tagRepository;
    private readonly IRepository<UserChannel> userChannelRepository;
    private readonly IRepository<Video> videoRepository;
    private readonly IRepository<VideoTag> videoTagRepository;

    public ChannelODataController(
        ILogger<ChannelODataController> logger,
        IUserContextService userContextService,
        VideoMetadataProviderFactory metadataProviderFactory,
        IBackgroundJobClient backgroundJobClient,
        IRepository<Channel> channelRepository,
        IRepository<Tag> tagRepository,
        IRepository<UserChannel> userChannelRepository,
        IRepository<Video> videoRepository,
        IRepository<VideoTag> videoTagRepository)
    {
        this.logger = logger;
        this.userContextService = userContextService;
        this.backgroundJobClient = backgroundJobClient;
        this.metadataProviderFactory = metadataProviderFactory;
        this.channelRepository = channelRepository;
        this.tagRepository = tagRepository;
        this.userChannelRepository = userChannelRepository;
        this.videoRepository = videoRepository;
        this.videoTagRepository = videoTagRepository;
    }

    [HttpDelete]
    public async Task<IActionResult> Delete([FromRoute] int key) // Ubsubscribe
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            // Find the user's subscription
            var userChannel = await userChannelRepository.FindOneAsync(new SearchOptions<UserChannel>
            {
                Query = x =>
                    x.UserId == userId &&
                    x.ChannelId == key
            });

            if (userChannel is null)
            {
                return NotFound();
            }

            // Remove user's subscription
            await userChannelRepository.DeleteAsync(userChannel);
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("User {UserId} unsubscribed from channel {ChannelId}", userId, key);
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error deleting channel subscription {ChannelId}", key);
            }

            return StatusCode(500, "An error occurred while unsubscribing from the channel");
        }
    }

    [EnableQuery]
    public async Task<IActionResult> Get()
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            bool isAdmin = userContextService.IsAdministrator();

            if (isAdmin)
            {
                // Admins see all channels
                var allChannels = await channelRepository.FindAsync(new SearchOptions<Channel>());
                return Ok(allChannels);
            }
            else
            {
                // Regular users only see their subscribed channels
                var channelIds = (await userChannelRepository.FindAsync(new SearchOptions<UserChannel>
                {
                    Query = x => x.UserId == userId
                }, x => x.ChannelId)).ToList();

                var channels = await channelRepository.FindAsync(new SearchOptions<Channel>
                {
                    Query = x => channelIds.Contains(x.Id)
                });

                return Ok(channels);
            }
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving channels");
            }

            return StatusCode(500, "An error occurred while retrieving channels");
        }
    }

    [EnableQuery]
    public async Task<IActionResult> Get([FromRoute] int key)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            bool isAdmin = userContextService.IsAdministrator();
            var channel = await channelRepository.FindOneAsync(key);

            if (channel is null)
            {
                return NotFound();
            }

            // Check if user has access to this channel
            if (!isAdmin)
            {
                var userChannel = await userChannelRepository.FindOneAsync(new SearchOptions<UserChannel>
                {
                    Query = x =>
                        x.UserId == userId &&
                        x.ChannelId == key
                });

                if (userChannel is null)
                {
                    return Forbid();
                }
            }

            return Ok(channel);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving channel {ChannelId}", key);
            }

            return StatusCode(500, "An error occurred while retrieving the channel");
        }
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] Channel channel)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            ChannelMetadata? channelMetadata = null;

            var existingChannel = await channelRepository.FindOneAsync(new SearchOptions<Channel>
            {
                Query = x => x.Url == channel.Url
            });

            if (existingChannel is null)
            {
                var provider = metadataProviderFactory.GetProviderByPlatform(channel.Platform);
                if (provider is null)
                {
                    if (logger.IsEnabled(LogLevel.Error))
                    {
                        logger.LogError("No metadata provider found for platform: {Platform}", channel.Platform);
                    }

                    return BadRequest();
                }

                // Update channel metadata
                channelMetadata = await provider.GetChannelMetadataAsync(channel.Url);
                if (channelMetadata is null)
                {
                    if (logger.IsEnabled(LogLevel.Error))
                    {
                        logger.LogError("Unable to retrieve metadata for channel: {Url}", channel.Url);
                    }

                    return BadRequest();
                }

                // Check if channel already exists by Url
                existingChannel = await channelRepository.FindOneAsync(new SearchOptions<Channel>
                {
                    Query = x => x.ChannelId == channelMetadata.ChannelId
                });
            }

            int channelDbId;

            if (existingChannel is not null)
            {
                // Channel exists, just subscribe the user
                channelDbId = existingChannel.Id;
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Channel {ChannelId} already exists, subscribing user {UserId}", channel.ChannelId, userId);
                }
            }
            else
            {
                // Create new channel
                channel.ChannelId = channelMetadata!.ChannelId;
                channel.Name = channelMetadata.Name;
                channel.Description = channelMetadata.Description;
                channel.ThumbnailUrl = channelMetadata.ThumbnailUrl;
                channel.SubscriberCount = channelMetadata.SubscriberCount;
                channel.SubscribedAt = DateTime.UtcNow;
                await channelRepository.InsertAsync(channel);
                channelDbId = channel.Id;
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Created new channel {ChannelId} for user {UserId}", channel.ChannelId, userId);
                }
            }

            // Check if user already subscribed
            var existingSubscription = await userChannelRepository.FindOneAsync(new SearchOptions<UserChannel>
            {
                Query = x =>
                    x.UserId == userId &&
                    x.ChannelId == channelDbId
            });

            if (existingSubscription is null)
            {
                // Create subscription
                await userChannelRepository.InsertAsync(new UserChannel
                {
                    UserId = userId,
                    ChannelId = channelDbId,
                    SubscribedAt = DateTime.UtcNow
                });

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("User {UserId} subscribed to channel {ChannelId}", userId, channelDbId);
                }
            }

            // Remove "standalone" tags from user's videos in this channel now that they're subscribed
            await RemoveStandaloneTagsForChannelAsync(userId, channelDbId);

            // Queue sync job for the channel
            backgroundJobClient.Enqueue<ChannelSyncJob>(job => job.ExecuteAsync(channelDbId, CancellationToken.None));

            // Return the channel (either existing or new)
            var resultChannel = await channelRepository.FindOneAsync(channelDbId);
            return Created(resultChannel);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error creating/subscribing to channel");
            }

            return StatusCode(500, "An error occurred while subscribing to the channel");
        }
    }

    private async Task RemoveStandaloneTagsForChannelAsync(string userId, int channelDbId)
    {
        try
        {
            var standaloneTag = await tagRepository.FindOneAsync(new SearchOptions<Tag>
            {
                Query = x => x.UserId == userId && x.Name == Constants.StandaloneTag
            });

            if (standaloneTag is null)
            {
                return;
            }

            // Get all video IDs in this channel
            var videoIds = (await videoRepository.FindAsync(
                new SearchOptions<Video> { Query = x => x.ChannelId == channelDbId },
                x => x.Id)).ToList();

            if (videoIds.Count == 0)
            {
                return;
            }

            // Remove standalone VideoTag entries for those videos
            var tagsToRemove = await videoTagRepository.FindAsync(new SearchOptions<VideoTag>
            {
                Query = x => x.TagId == standaloneTag.Id && videoIds.Contains(x.VideoId)
            });

            foreach (var vt in tagsToRemove)
            {
                await videoTagRepository.DeleteAsync(vt);
            }

            if (logger.IsEnabled(LogLevel.Information) && tagsToRemove.Count > 0)
            {
                logger.LogInformation(
                    "Removed standalone tags from {Count} video(s) in channel {ChannelId} for user {UserId}",
                    tagsToRemove.Count, channelDbId, userId);
            }
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(ex, "Failed to remove standalone tags for channel {ChannelId} user {UserId}",
                    channelDbId, userId);
            }
        }
    }
}