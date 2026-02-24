using Hangfire;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace MyVideoArchive.Controllers.OData;

[Authorize]
public class ChannelODataController : ODataController
{
    private readonly ILogger<ChannelODataController> logger;
    private readonly IUserContextService userContextService;
    private readonly VideoMetadataProviderFactory metadataProviderFactory;
    private readonly IBackgroundJobClient backgroundJobClient;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<UserChannel> userChannelRepository;

    public ChannelODataController(
        ILogger<ChannelODataController> logger,
        IUserContextService userContextService,
        VideoMetadataProviderFactory metadataProviderFactory,
        IBackgroundJobClient backgroundJobClient,
        IRepository<Channel> channelRepository,
        IRepository<UserChannel> userChannelRepository)
    {
        this.logger = logger;
        this.userContextService = userContextService;
        this.backgroundJobClient = backgroundJobClient;
        this.metadataProviderFactory = metadataProviderFactory;
        this.channelRepository = channelRepository;
        this.userChannelRepository = userChannelRepository;
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
            var channelMetadata = await provider.GetChannelMetadataAsync(channel.Url);
            if (channelMetadata is null)
            {
                if (logger.IsEnabled(LogLevel.Error))
                {
                    logger.LogError("Unable to retrieve metadata for channel: {Url}", channel.Url);
                }

                return BadRequest();
            }

            int channelDbId;

            // Check if channel already exists by Url
            var existingChannel = await channelRepository.FindOneAsync(new SearchOptions<Channel>
            {
                Query = x => x.Url == channel.Url
            });

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
                channel.ChannelId = channelMetadata.ChannelId;
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

    [HttpDelete]
    public async Task<IActionResult> Delete([FromRoute] int key)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            bool isAdmin = userContextService.IsAdministrator();

            // Find the user's subscription
            var userChannel = await userChannelRepository.FindOneAsync(new SearchOptions<UserChannel>
            {
                Query = x =>
                    x.UserId == userId &&
                    x.ChannelId == key
            });

            if (userChannel is null && !isAdmin)
            {
                return NotFound();
            }

            if (userChannel is not null)
            {
                // Remove user's subscription
                await userChannelRepository.DeleteAsync(userChannel);
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("User {UserId} unsubscribed from channel {ChannelId}", userId, key);
                }
            }

            // Optionally: If no users are subscribed to this channel, delete it
            // (Only if admin or if this was the last subscription)
            bool hasRemainingSubscriptions = await userChannelRepository.ExistsAsync(x => x.ChannelId == key);

            if (hasRemainingSubscriptions)
            {
                // No one is subscribed, delete the channel
                var channelExists = await channelRepository.ExistsAsync(x => x.Id == key);
                if (channelExists)
                {
                    await channelRepository.DeleteAsync(x => x.Id == key);
                    if (logger.IsEnabled(LogLevel.Information))
                    {
                        logger.LogInformation("Deleted channel {ChannelId} as no users are subscribed", key);
                    }
                }
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
}