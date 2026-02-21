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
    private readonly ILogger<ChannelSyncJob> logger;
    private readonly VideoMetadataProviderFactory metadataProviderFactory;
    private readonly IBackgroundJobClient backgroundJobClient;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<Video> videoRepository;

    public ChannelSyncJob(
        ILogger<ChannelSyncJob> logger,
        VideoMetadataProviderFactory metadataProviderFactory,
        IBackgroundJobClient backgroundJobClient,
        IRepository<Channel> channelRepository,
        IRepository<Video> videoRepository)
    {
        this.logger = logger;
        this.metadataProviderFactory = metadataProviderFactory;
        this.backgroundJobClient = backgroundJobClient;
        this.channelRepository = channelRepository;
        this.videoRepository = videoRepository;
    }

    [HangfireSkipWhenPreviousInstanceIsRunningFilter]
    public async Task ExecuteAsync(int channelId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting channel sync job for channel ID: {ChannelId}", channelId);

        try
        {
            var channel = await channelRepository.FindOneAsync(new SearchOptions<Channel>
            {
                CancellationToken = cancellationToken,
                Query = x => x.Id == channelId,
                Include = query => query.Include(x => x.Videos)
            });

            if (channel is null)
            {
                logger.LogWarning("Channel with ID {ChannelId} not found", channelId);
                return;
            }

            // Get the appropriate metadata provider
            var provider = metadataProviderFactory.GetProviderByPlatform(channel.Platform);
            if (provider is null)
            {
                logger.LogError("No metadata provider found for platform: {Platform}", channel.Platform);
                return;
            }

            // Update channel metadata
            var channelMetadata = await provider.GetChannelMetadataAsync(channel.Url, cancellationToken);
            if (channelMetadata is not null)
            {
                channel.Name = channelMetadata.Name;
                channel.Description = channelMetadata.Description;
                channel.ThumbnailUrl = channelMetadata.ThumbnailUrl;
                channel.SubscriberCount = channelMetadata.SubscriberCount;
                channel.VideoCount = channelMetadata.VideoCount;
            }

            // Get all videos from the channel
            var videoMetadataList = await provider.GetChannelVideosAsync(channel.Url, cancellationToken);
            logger.LogInformation("Found {Count} videos for channel {ChannelId}", videoMetadataList.Count, channelId);

            // Process each video
            var existingVideoIds = channel.Videos.Select(v => v.VideoId).ToHashSet();
            int newVideosCount = 0;

            var videoUpdates = new List<Video>();
            var videoInserts = new List<Video>();
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
                    videoUpdates.Add(existingVideo);
                }
                else
                {
                    // Create new video entry (without auto-downloading)
                    videoInserts.Add(new Video
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
                    });
                    newVideosCount++;

                    // Note: Videos are no longer auto-downloaded.
                    // Users must select videos to download from the Available tab.
                }
            }

            await videoRepository.InsertAsync(videoInserts, ContextOptions.ForCancellationToken(cancellationToken));
            await videoRepository.UpdateAsync(videoUpdates, ContextOptions.ForCancellationToken(cancellationToken));

            // Update last checked timestamp
            channel.LastChecked = DateTime.UtcNow;
            await channelRepository.UpdateAsync(channel, ContextOptions.ForCancellationToken(cancellationToken));

            logger.LogInformation(
                "Channel sync completed for {ChannelId}. New videos: {NewCount}, Total videos: {TotalCount}",
                channelId, newVideosCount, videoMetadataList.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error syncing channel {ChannelId}", channelId);
            throw;
        }
    }

    [HangfireSkipWhenPreviousInstanceIsRunningFilter]
    public async Task SyncAllChannelsAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting sync for all channels");

        try
        {
            var channels = await channelRepository.FindAsync(new SearchOptions<Channel>
            {
                CancellationToken = cancellationToken
            });

            foreach (var channel in channels)
            {
                // Queue individual channel sync jobs
                backgroundJobClient.Enqueue<ChannelSyncJob>(job =>
                    job.ExecuteAsync(channel.Id, CancellationToken.None));
            }

            logger.LogInformation("Queued sync jobs for {Count} channels", channels.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error queuing channel sync jobs");
            throw;
        }
    }
}