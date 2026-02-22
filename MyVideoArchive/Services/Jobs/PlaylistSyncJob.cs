using Hangfire;

namespace MyVideoArchive.Services.Jobs;

/// <summary>
/// Hangfire job for synchronizing playlists with their source platforms
/// </summary>
public class PlaylistSyncJob
{
    private readonly ILogger<PlaylistSyncJob> logger;
    private readonly VideoMetadataProviderFactory metadataProviderFactory;
    private readonly IBackgroundJobClient backgroundJobClient;
    private readonly IRepository<Playlist> playlistRepository;
    private readonly IRepository<PlaylistVideo> playlistVideoRepository;
    private readonly IRepository<UserPlaylist> userPlaylistRepository;
    private readonly IRepository<Video> videoRepository;

    public PlaylistSyncJob(
        ILogger<PlaylistSyncJob> logger,
        VideoMetadataProviderFactory metadataProviderFactory,
        IBackgroundJobClient backgroundJobClient,
        IRepository<Playlist> playlistRepository,
        IRepository<PlaylistVideo> playlistVideoRepository,
        IRepository<UserPlaylist> userPlaylistRepository,
        IRepository<Video> videoRepository)
    {
        this.logger = logger;
        this.metadataProviderFactory = metadataProviderFactory;
        this.backgroundJobClient = backgroundJobClient;
        this.playlistRepository = playlistRepository;
        this.playlistVideoRepository = playlistVideoRepository;
        this.userPlaylistRepository = userPlaylistRepository;
        this.videoRepository = videoRepository;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 3600)] // 1 hour timeout
    public async Task ExecuteAsync(int playlistId, CancellationToken cancellationToken = default)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Starting playlist sync job for playlist ID: {PlaylistId}", playlistId);
        }

        try
        {
            var playlist = await playlistRepository.FindOneAsync(new SearchOptions<Playlist>
            {
                CancellationToken = cancellationToken,

                Query = x => x.Id == playlistId,

                Include = query => query
                    .Include(x => x.PlaylistVideos)
            });

            if (playlist is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Playlist with ID {PlaylistId} not found", playlistId);
                }

                return;
            }

            // Get the appropriate metadata provider
            var provider = metadataProviderFactory.GetProviderByPlatform(playlist.Platform);
            if (provider is null)
            {
                if (logger.IsEnabled(LogLevel.Error))
                {
                    logger.LogError("No metadata provider found for platform: {Platform}", playlist.Platform);
                }

                return;
            }

            // Update playlist metadata
            var playlistMetadata = await provider.GetPlaylistMetadataAsync(playlist.Url, cancellationToken);
            if (playlistMetadata is not null)
            {
                playlist.Name = playlistMetadata.Name;
                playlist.Description = playlistMetadata.Description;
            }

            bool hasAnySubs = await userPlaylistRepository.CountAsync(
                x => x.PlaylistId == playlistId,
                ContextOptions.ForCancellationToken(cancellationToken)) > 0;

            if (hasAnySubs)
            {
                // Get all videos from the playlist
                var videoMetadataList = await provider.GetPlaylistVideosAsync(playlist.Url, cancellationToken);
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Found {Count} videos for playlist {PlaylistId}", videoMetadataList.Count, playlistId);
                }

                // Process each video
                var existingPlaylistVideoIds = playlist.PlaylistVideos.Select(vp => vp.VideoId).ToHashSet();
                int newVideosCount = 0;
                int videoOrder = 0; // Track the order of videos as they appear in the playlist

                var videoUpdates = new List<Video>();
                var videoInserts = new List<Video>();
                var playlistVideoInserts = new List<PlaylistVideo>();

                foreach (var videoMetadata in videoMetadataList)
                {
                    videoOrder++; // Increment order for each video in playlist

                    // Check if video exists in the database (could be in another playlist or standalone)
                    var existingVideo = await videoRepository.FindOneAsync(new SearchOptions<Video>
                    {
                        CancellationToken = cancellationToken,
                        Query = x =>
                            x.Platform == videoMetadata.Platform &&
                            x.VideoId == videoMetadata.VideoId
                    });

                    int videoId;

                    if (existingVideo is not null)
                    {
                        // Update existing video metadata
                        existingVideo.Title = videoMetadata.Title;
                        existingVideo.Description = videoMetadata.Description;
                        existingVideo.ThumbnailUrl = videoMetadata.ThumbnailUrl;
                        existingVideo.Duration = videoMetadata.Duration;
                        existingVideo.ViewCount = videoMetadata.ViewCount;
                        existingVideo.LikeCount = videoMetadata.LikeCount;

                        videoUpdates.Add(existingVideo);
                        videoId = existingVideo.Id;
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
                            ChannelId = playlist.ChannelId,
                            IsIgnored = false,
                            IsQueued = false
                        };

                        videoInserts.Add(newVideo);
                        videoId = newVideo.Id;
                        newVideosCount++;
                    }

                    // Associate video with this playlist if not already associated
                    if (!existingPlaylistVideoIds.Contains(videoId))
                    {
                        var playlistVideo = new PlaylistVideo
                        {
                            PlaylistId = playlistId,
                            VideoId = videoId,
                            Order = videoOrder // Set the original order
                        };

                        if (await playlistVideoRepository.CountAsync(x => x.PlaylistId == playlistId && x.VideoId == videoId) == 0)
                        {
                            playlistVideoInserts.Add(playlistVideo);
                        }
                    }
                }

                await videoRepository.InsertAsync(videoInserts, ContextOptions.ForCancellationToken(cancellationToken));
                await videoRepository.UpdateAsync(videoUpdates, ContextOptions.ForCancellationToken(cancellationToken));
                await playlistVideoRepository.InsertAsync(playlistVideoInserts, ContextOptions.ForCancellationToken(cancellationToken));

                playlist.VideoCount = playlistVideoRepository.Count(
                    x => x.PlaylistId == playlistId,
                    ContextOptions.ForCancellationToken(cancellationToken));
            }

            // Update last checked timestamp
            playlist.LastChecked = DateTime.UtcNow;
            await playlistRepository.UpdateAsync(playlist, ContextOptions.ForCancellationToken(cancellationToken));

            //logger.LogInformation(
            //    "Playlist sync completed for {PlaylistId}. New videos: {NewCount}, Total videos: {TotalCount}",
            //    playlistId, newVideosCount, videoMetadataList.Count);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error syncing playlist {PlaylistId}", playlistId);
            }

            throw;
        }
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)] // 10 minutes timeout
    public async Task SyncAllPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Starting sync for all playlists");
        }

        try
        {
            var playlists = await playlistRepository.FindAsync(new SearchOptions<Playlist>
            {
                CancellationToken = cancellationToken
            });

            foreach (var playlist in playlists)
            {
                // Queue individual playlist sync jobs
                backgroundJobClient.Enqueue<PlaylistSyncJob>(job =>
                    job.ExecuteAsync(playlist.Id, CancellationToken.None));
            }

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Queued sync jobs for {Count} playlists", playlists.Count);
            }
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error queuing playlist sync jobs");
            }

            throw;
        }
    }
}