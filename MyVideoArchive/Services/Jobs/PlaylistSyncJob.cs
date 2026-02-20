using Extenso.Data.Entity;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MyVideoArchive.Data.Entities;

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
    private readonly IRepository<Video> videoRepository;
    private readonly IRepository<PlaylistVideo> videoPlaylistRepository;
    private readonly IRepository<UserPlaylist> userPlaylistRepository;

    public PlaylistSyncJob(
        ILogger<PlaylistSyncJob> logger,
        VideoMetadataProviderFactory metadataProviderFactory,
        IBackgroundJobClient backgroundJobClient,
        IRepository<Playlist> playlistRepository,
        IRepository<Video> videoRepository,
        IRepository<PlaylistVideo> videoPlaylistRepository,
        IRepository<UserPlaylist> userPlaylistRepository)
    {
        this.logger = logger;
        this.metadataProviderFactory = metadataProviderFactory;
        this.backgroundJobClient = backgroundJobClient;
        this.playlistRepository = playlistRepository;
        this.videoRepository = videoRepository;
        this.videoPlaylistRepository = videoPlaylistRepository;
        this.userPlaylistRepository = userPlaylistRepository;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 3600)] // 1 hour timeout
    public async Task ExecuteAsync(int playlistId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting playlist sync job for playlist ID: {PlaylistId}", playlistId);

        try
        {
            var playlist = await playlistRepository.FindOneAsync(new SearchOptions<Playlist>
            {
                CancellationToken = cancellationToken,
                Query = x => x.Id == playlistId,
                Include = query => query.Include(x => x.VideoPlaylists).Include(x => x.Channel)
            });

            if (playlist == null)
            {
                logger.LogWarning("Playlist with ID {PlaylistId} not found", playlistId);
                return;
            }

            // Get the appropriate metadata provider
            var provider = metadataProviderFactory.GetProviderByPlatform(playlist.Platform);
            if (provider == null)
            {
                logger.LogError("No metadata provider found for platform: {Platform}", playlist.Platform);
                return;
            }

            // Update playlist metadata
            var playlistMetadata = await provider.GetPlaylistMetadataAsync(playlist.Url, cancellationToken);
            if (playlistMetadata != null)
            {
                playlist.Name = playlistMetadata.Name;
                playlist.Description = playlistMetadata.Description;
                playlist.VideoCount = playlistMetadata.VideoCount;
            }

            bool hasAnySubs = await userPlaylistRepository.CountAsync(
                up => up.PlaylistId == playlistId,
                ContextOptions.ForCancellationToken(cancellationToken)) > 0;

            if (hasAnySubs)
            {
                // Get all videos from the playlist
                var videoMetadataList = await provider.GetPlaylistVideosAsync(playlist.Url, cancellationToken);
                logger.LogInformation("Found {Count} videos for playlist {PlaylistId}", videoMetadataList.Count, playlistId);

                // Process each video
                var existingPlaylistVideoIds = playlist.VideoPlaylists.Select(vp => vp.VideoId).ToHashSet();
                int newVideosCount = 0;
                int videoOrder = 0; // Track the order of videos as they appear in the playlist

                var videoUpdates = new List<Video>();
                var videoInserts = new List<Video>();
                var videoPlaylistInserts = new List<PlaylistVideo>();

                foreach (var videoMetadata in videoMetadataList)
                {
                    videoOrder++; // Increment order for each video in playlist

                    // Check if video exists in the database (could be in another playlist or standalone)
                    var existingVideo = await videoRepository.FindOneAsync(new SearchOptions<Video>
                    {
                        CancellationToken = cancellationToken,
                        Query = v => v.Platform == videoMetadata.Platform && v.VideoId == videoMetadata.VideoId
                    });

                    int videoId;

                    if (existingVideo != null)
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
                        var videoPlaylist = new PlaylistVideo
                        {
                            PlaylistId = playlistId,
                            VideoId = videoId,
                            Order = videoOrder // Set the original order
                        };

                        if (await videoPlaylistRepository.CountAsync(x => x.PlaylistId == playlistId && x.VideoId == videoId) == 0)
                        {
                            videoPlaylistInserts.Add(videoPlaylist);
                        }
                    }
                }

                await videoRepository.InsertAsync(videoInserts, ContextOptions.ForCancellationToken(cancellationToken));
                await videoRepository.UpdateAsync(videoUpdates, ContextOptions.ForCancellationToken(cancellationToken));
                await videoPlaylistRepository.InsertAsync(videoPlaylistInserts, ContextOptions.ForCancellationToken(cancellationToken));
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
            logger.LogError(ex, "Error syncing playlist {PlaylistId}", playlistId);
            throw;
        }
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)] // 10 minutes timeout
    public async Task SyncAllPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting sync for all playlists");

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

            logger.LogInformation("Queued sync jobs for {Count} playlists", playlists.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error queuing playlist sync jobs");
            throw;
        }
    }
}
