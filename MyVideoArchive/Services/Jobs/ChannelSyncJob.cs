using Extenso.Data.Entity;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MyVideoArchive.Data.Entities;
using MyVideoArchive.Infrastructure;

namespace MyVideoArchive.Services.Jobs;

/// <summary>
/// Hangfire job for synchronizing channels with their source platforms
/// </summary>
public class ChannelSyncJob
{
    private readonly ILogger<ChannelSyncJob> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly VideoMetadataProviderFactory _metadataProviderFactory;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public ChannelSyncJob(
        ILogger<ChannelSyncJob> logger,
        IServiceProvider serviceProvider,
        VideoMetadataProviderFactory metadataProviderFactory,
        IBackgroundJobClient backgroundJobClient)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _metadataProviderFactory = metadataProviderFactory;
        _backgroundJobClient = backgroundJobClient;
    }

    [HangfireSkipWhenPreviousInstanceIsRunningFilter]
    //[DisableConcurrentExecution(timeoutInSeconds: 3600)] // 1 hour timeout
    public async Task ExecuteAsync(int channelId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting channel sync job for channel ID: {ChannelId}", channelId);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var channelRepository = scope.ServiceProvider.GetRequiredService<IRepository<Channel>>();
            var videoRepository = scope.ServiceProvider.GetRequiredService<IRepository<Video>>();

            var channel = await channelRepository.FindOneAsync(new SearchOptions<Channel>
            {
                CancellationToken = cancellationToken,
                Query = x => x.Id == channelId,
                Include = query => query.Include(x => x.Videos)
            });

            if (channel == null)
            {
                _logger.LogWarning("Channel with ID {ChannelId} not found", channelId);
                return;
            }

            // Get the appropriate metadata provider
            var provider = _metadataProviderFactory.GetProviderByPlatform(channel.Platform);
            if (provider == null)
            {
                _logger.LogError("No metadata provider found for platform: {Platform}", channel.Platform);
                return;
            }

            // Update channel metadata
            var channelMetadata = await provider.GetChannelMetadataAsync(channel.Url, cancellationToken);
            if (channelMetadata != null)
            {
                channel.Name = channelMetadata.Name;
                channel.Description = channelMetadata.Description;
                channel.ThumbnailUrl = channelMetadata.ThumbnailUrl;
                channel.SubscriberCount = channelMetadata.SubscriberCount;
                channel.VideoCount = channelMetadata.VideoCount;
            }

            // Get all videos from the channel
            var videoMetadataList = await provider.GetChannelVideosAsync(channel.Url, cancellationToken);
            _logger.LogInformation("Found {Count} videos for channel {ChannelId}", videoMetadataList.Count, channelId);

            // Process each video
            var existingVideoIds = channel.Videos.Select(v => v.VideoId).ToHashSet();
            var newVideosCount = 0;

            foreach (var videoMetadata in videoMetadataList)
            {
                if (existingVideoIds.Contains(videoMetadata.VideoId))
                {
                    // Update existing video metadata
                    var existingVideo = channel.Videos.First(v => v.VideoId == videoMetadata.VideoId);
                    existingVideo.Title = videoMetadata.Title;
                    existingVideo.Description = videoMetadata.Description;
                    existingVideo.ThumbnailUrl = videoMetadata.ThumbnailUrl;
                    existingVideo.Duration = videoMetadata.Duration;
                    existingVideo.ViewCount = videoMetadata.ViewCount;
                    existingVideo.LikeCount = videoMetadata.LikeCount;

                    await videoRepository.UpdateAsync(existingVideo);
                }
                else
                {
                    // Create new video entry (without auto-downloading)
                    var newVideo = new Video
                    {
                        VideoId = videoMetadata.VideoId,
                        Title = videoMetadata.Title,
                        Description = videoMetadata.Description,
                        Url = videoMetadata.Url,
                        ThumbnailUrl = videoMetadata.ThumbnailUrl,
                        Platform = videoMetadata.Platform,
                        Duration = videoMetadata.Duration,
                        UploadDate = videoMetadata.UploadDate,
                        ViewCount = videoMetadata.ViewCount,
                        LikeCount = videoMetadata.LikeCount,
                        ChannelId = channelId,
                        IsIgnored = false
                    };

                    await videoRepository.InsertAsync(newVideo);
                    newVideosCount++;

                    // Note: Videos are no longer auto-downloaded. 
                    // Users must select videos to download from the Available tab.
                }
            }

            // Update last checked timestamp
            channel.LastChecked = DateTime.UtcNow;
            await channelRepository.UpdateAsync(channel);

            _logger.LogInformation(
                "Channel sync completed for {ChannelId}. New videos: {NewCount}, Total videos: {TotalCount}",
                channelId, newVideosCount, videoMetadataList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing channel {ChannelId}", channelId);
            throw;
        }
    }

    [HangfireSkipWhenPreviousInstanceIsRunningFilter]
    //[DisableConcurrentExecution(timeoutInSeconds: 600)] // 10 minutes timeout
    public async Task SyncAllChannelsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting sync for all channels");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var channelRepository = scope.ServiceProvider.GetRequiredService<IRepository<Channel>>();

            var channels = await channelRepository.FindAsync(new SearchOptions<Channel>
            {
                CancellationToken = cancellationToken
            });

            foreach (var channel in channels)
            {
                // Queue individual channel sync jobs
                _backgroundJobClient.Enqueue<ChannelSyncJob>(job =>
                    job.ExecuteAsync(channel.Id, CancellationToken.None));
            }

            _logger.LogInformation("Queued sync jobs for {Count} channels", channels.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error queuing channel sync jobs");
            throw;
        }
    }
}
