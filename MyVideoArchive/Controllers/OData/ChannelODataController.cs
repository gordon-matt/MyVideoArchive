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
    private readonly ITagService tagService;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<UserChannel> userChannelRepository;

    public ChannelODataController(
        ILogger<ChannelODataController> logger,
        IUserContextService userContextService,
        VideoMetadataProviderFactory metadataProviderFactory,
        IBackgroundJobClient backgroundJobClient,
        ITagService tagService,
        IRepository<Channel> channelRepository,
        IRepository<UserChannel> userChannelRepository)
    {
        this.logger = logger;
        this.userContextService = userContextService;
        this.backgroundJobClient = backgroundJobClient;
        this.metadataProviderFactory = metadataProviderFactory;
        this.tagService = tagService;
        this.channelRepository = channelRepository;
        this.userChannelRepository = userChannelRepository;
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
            var userChannelExists = await userChannelRepository.ExistsAsync(x => x.UserId == userId && x.ChannelId == key);
            if (userChannelExists)
            {
                return NotFound();
            }

            // Remove user's subscription
            await userChannelRepository.DeleteAsync(x => x.UserId == userId && x.ChannelId == key);
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
                var channels = await userChannelRepository.FindAsync(new SearchOptions<UserChannel>
                {
                    Query = x => x.UserId == userId,
                    Include = query => query.Include(x => x.Channel)
                }, x => x.Channel);

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

            var channel = await channelRepository.FindOneAsync(key);
            if (channel is null)
            {
                return NotFound();
            }

            bool isAdmin = userContextService.IsAdministrator();
            if (!isAdmin)
            {
                // Check if user has access to this channel
                var userChannelExists = await userChannelRepository.ExistsAsync(x => x.UserId == userId && x.ChannelId == key);
                if (!userChannelExists)
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

            // Check if channel already exists by Url
            int existingChannelId = await channelRepository.FindOneAsync(new SearchOptions<Channel>
            {
                Query = x => x.Url == channel.Url
            }, x => x.Id);

            if (existingChannelId == 0)
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

                // Check if channel already exists by Channel ID
                existingChannelId = await channelRepository.FindOneAsync(new SearchOptions<Channel>
                {
                    Query = x => x.ChannelId == channelMetadata.ChannelId
                }, x => x.Id);
            }

            int channelDbId;
            bool channelAlreadyExists = existingChannelId > 0;

            if (channelAlreadyExists)
            {
                // Channel exists, just subscribe the user
                channelDbId = existingChannelId;
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
                channel = await channelRepository.InsertAsync(channel);
                channelDbId = channel.Id;

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Created new channel {ChannelId} for user {UserId}", channel.ChannelId, userId);
                }
            }

            // Check if user already subscribed
            bool subscriptionExists = await userChannelRepository.ExistsAsync(x =>
                x.UserId == userId &&
                x.ChannelId == channelDbId);

            if (!subscriptionExists)
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
            await tagService.RemoveStandaloneTagsForChannelAsync(userId, channelDbId);

            if (!channelAlreadyExists)
            {
                // Queue sync job for the channel
                backgroundJobClient.Enqueue<ChannelSyncJob>(job => job.ExecuteAsync(channelDbId, CancellationToken.None));
            }

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
}