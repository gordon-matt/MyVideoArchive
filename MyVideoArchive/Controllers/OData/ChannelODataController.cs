using Extenso.Data.Entity;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using MyVideoArchive.Data.Entities;
using MyVideoArchive.Services;
using MyVideoArchive.Services.Jobs;

namespace MyVideoArchive.Controllers.OData;

[Authorize]
public class ChannelODataController : ODataController
{
    private readonly IRepository<Channel> _channelRepository;
    private readonly IRepository<UserChannel> _userChannelRepository;
    private readonly IUserContextService _userContext;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<ChannelODataController> _logger;

    public ChannelODataController(
        IRepository<Channel> channelRepository,
        IRepository<UserChannel> userChannelRepository,
        IUserContextService userContext,
        IBackgroundJobClient backgroundJobClient,
        ILogger<ChannelODataController> logger)
    {
        _channelRepository = channelRepository;
        _userChannelRepository = userChannelRepository;
        _userContext = userContext;
        _backgroundJobClient = backgroundJobClient;
        _logger = logger;
    }

    [EnableQuery]
    public async Task<IActionResult> Get()
    {
        try
        {
            string? userId = _userContext.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            bool isAdmin = _userContext.IsAdministrator();

            if (isAdmin)
            {
                // Admins see all channels
                var allChannels = await _channelRepository.FindAsync(new SearchOptions<Channel>());
                return Ok(allChannels);
            }
            else
            {
                // Regular users only see their subscribed channels
                var userChannels = await _userChannelRepository.FindAsync(new SearchOptions<UserChannel>
                {
                    Query = uc => uc.UserId == userId
                });

                var channelIds = userChannels.Select(uc => uc.ChannelId).ToList();
                var channels = await _channelRepository.FindAsync(new SearchOptions<Channel>
                {
                    Query = c => channelIds.Contains(c.Id)
                });

                return Ok(channels);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving channels");
            return StatusCode(500, "An error occurred while retrieving channels");
        }
    }

    [EnableQuery]
    public async Task<IActionResult> Get([FromRoute] int key)
    {
        try
        {
            string? userId = _userContext.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            bool isAdmin = _userContext.IsAdministrator();
            var channel = await _channelRepository.FindOneAsync(key);

            if (channel == null)
            {
                return NotFound();
            }

            // Check if user has access to this channel
            if (!isAdmin)
            {
                var userChannel = await _userChannelRepository.FindOneAsync(new SearchOptions<UserChannel>
                {
                    Query = uc => uc.UserId == userId && uc.ChannelId == key
                });

                if (userChannel == null)
                {
                    return Forbid();
                }
            }

            return Ok(channel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving channel {ChannelId}", key);
            return StatusCode(500, "An error occurred while retrieving the channel");
        }
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] Channel channel)
    {
        try
        {
            string? userId = _userContext.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Check if channel already exists by ChannelId
            var existingChannel = await _channelRepository.FindOneAsync(new SearchOptions<Channel>
            {
                Query = c => c.ChannelId == channel.ChannelId
            });

            int channelDbId;

            if (existingChannel != null)
            {
                // Channel exists, just subscribe the user
                channelDbId = existingChannel.Id;
                _logger.LogInformation("Channel {ChannelId} already exists, subscribing user {UserId}", channel.ChannelId, userId);
            }
            else
            {
                // Create new channel
                channel.SubscribedAt = DateTime.UtcNow;
                channel.Platform = "YouTube"; // Default platform
                await _channelRepository.InsertAsync(channel);
                channelDbId = channel.Id;
                _logger.LogInformation("Created new channel {ChannelId} for user {UserId}", channel.ChannelId, userId);
            }

            // Check if user already subscribed
            var existingSubscription = await _userChannelRepository.FindOneAsync(new SearchOptions<UserChannel>
            {
                Query = uc => uc.UserId == userId && uc.ChannelId == channelDbId
            });

            if (existingSubscription == null)
            {
                // Create subscription
                await _userChannelRepository.InsertAsync(new UserChannel
                {
                    UserId = userId,
                    ChannelId = channelDbId,
                    SubscribedAt = DateTime.UtcNow
                });

                _logger.LogInformation("User {UserId} subscribed to channel {ChannelId}", userId, channelDbId);
            }

            // Queue sync job for the channel
            _backgroundJobClient.Enqueue<ChannelSyncJob>(job => job.ExecuteAsync(channelDbId, CancellationToken.None));

            // Return the channel (either existing or new)
            var resultChannel = await _channelRepository.FindOneAsync(channelDbId);
            return Created(resultChannel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating/subscribing to channel");
            return StatusCode(500, "An error occurred while subscribing to the channel");
        }
    }

    [HttpDelete]
    public async Task<IActionResult> Delete([FromRoute] int key)
    {
        try
        {
            string? userId = _userContext.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            bool isAdmin = _userContext.IsAdministrator();

            // Find the user's subscription
            var userChannel = await _userChannelRepository.FindOneAsync(new SearchOptions<UserChannel>
            {
                Query = uc => uc.UserId == userId && uc.ChannelId == key
            });

            if (userChannel == null && !isAdmin)
            {
                return NotFound();
            }

            if (userChannel != null)
            {
                // Remove user's subscription
                await _userChannelRepository.DeleteAsync(userChannel);
                _logger.LogInformation("User {UserId} unsubscribed from channel {ChannelId}", userId, key);
            }

            // Optionally: If no users are subscribed to this channel, delete it
            // (Only if admin or if this was the last subscription)
            var remainingSubscriptions = await _userChannelRepository.FindAsync(new SearchOptions<UserChannel>
            {
                Query = uc => uc.ChannelId == key
            });

            if (remainingSubscriptions.Count == 0)
            {
                // No one is subscribed, delete the channel
                var channel = await _channelRepository.FindOneAsync(key);
                if (channel != null)
                {
                    await _channelRepository.DeleteAsync(channel);
                    _logger.LogInformation("Deleted channel {ChannelId} as no users are subscribed", key);
                }
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting channel subscription {ChannelId}", key);
            return StatusCode(500, "An error occurred while unsubscribing from the channel");
        }
    }
}
