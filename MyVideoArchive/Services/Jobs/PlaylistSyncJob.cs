using Extenso.Data.Entity;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MyVideoArchive.Data;
using MyVideoArchive.Data.Entities;

namespace MyVideoArchive.Services.Jobs;

/// <summary>
/// Hangfire job for synchronizing playlists with their source platforms
/// </summary>
public class PlaylistSyncJob
{
    private readonly ILogger<PlaylistSyncJob> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly VideoMetadataProviderFactory _metadataProviderFactory;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public PlaylistSyncJob(
        ILogger<PlaylistSyncJob> logger,
        IServiceProvider serviceProvider,
        VideoMetadataProviderFactory metadataProviderFactory,
        IBackgroundJobClient backgroundJobClient)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _metadataProviderFactory = metadataProviderFactory;
        _backgroundJobClient = backgroundJobClient;
    }

    public async Task ExecuteAsync(int playlistId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting playlist sync job for playlist ID: {PlaylistId}", playlistId);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var playlistRepository = scope.ServiceProvider.GetRequiredService<IRepository<Playlist>>();
            var videoRepository = scope.ServiceProvider.GetRequiredService<IRepository<Video>>();

            var playlist = await playlistRepository.FindOneAsync(new SearchOptions<Playlist>
            {
                CancellationToken = cancellationToken,
                Query = x => x.Id == playlistId,
                Include = query => query.Include(x => x.Videos).Include(x => x.Channel)
            });

            if (playlist == null)
            {
                _logger.LogWarning("Playlist with ID {PlaylistId} not found", playlistId);
                return;
            }

            // Get the appropriate metadata provider
            var provider = _metadataProviderFactory.GetProviderByPlatform(playlist.Platform);
            if (provider == null)
            {
                _logger.LogError("No metadata provider found for platform: {Platform}", playlist.Platform);
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

            // Get all videos from the playlist
            var videoMetadataList = await provider.GetPlaylistVideosAsync(playlist.Url, cancellationToken);
            _logger.LogInformation("Found {Count} videos for playlist {PlaylistId}", videoMetadataList.Count, playlistId);

            // Process each video
            var existingVideoIds = playlist.Videos.Select(v => v.VideoId).ToHashSet();
            var newVideosCount = 0;

            foreach (var videoMetadata in videoMetadataList)
            {
                // Check if video exists in the database (could be in another playlist or standalone)
                var existingVideo = await videoRepository.FindOneAsync(new SearchOptions<Video>
                {
                    CancellationToken = cancellationToken,
                    Query = v => v.Platform == videoMetadata.Platform && v.VideoId == videoMetadata.VideoId
                });

                if (existingVideo != null)
                {
                    // Update existing video metadata
                    existingVideo.Title = videoMetadata.Title;
                    existingVideo.Description = videoMetadata.Description;
                    existingVideo.ThumbnailUrl = videoMetadata.ThumbnailUrl;
                    existingVideo.Duration = videoMetadata.Duration;
                    existingVideo.ViewCount = videoMetadata.ViewCount;
                    existingVideo.LikeCount = videoMetadata.LikeCount;

                    // Associate with this playlist if not already
                    if (existingVideo.PlaylistId != playlistId)
                    {
                        existingVideo.PlaylistId = playlistId;
                    }

                    await videoRepository.UpdateAsync(existingVideo);
                }
                else
                {
                    // Create new video entry
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
                        PlaylistId = playlistId
                    };

                    await videoRepository.InsertAsync(newVideo);
                    newVideosCount++;

                    // Queue download job for the new video
                    _backgroundJobClient.Enqueue<VideoDownloadJob>(job => 
                        job.ExecuteAsync(newVideo.Id, CancellationToken.None));
                }
            }

            // Update last checked timestamp
            playlist.LastChecked = DateTime.UtcNow;
            await playlistRepository.UpdateAsync(playlist);

            _logger.LogInformation(
                "Playlist sync completed for {PlaylistId}. New videos: {NewCount}, Total videos: {TotalCount}",
                playlistId, newVideosCount, videoMetadataList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing playlist {PlaylistId}", playlistId);
            throw;
        }
    }

    public async Task SyncAllPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting sync for all playlists");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var playlistRepository = scope.ServiceProvider.GetRequiredService<IRepository<Playlist>>();

            var playlists = await playlistRepository.FindAsync(new SearchOptions<Playlist>
            {
                CancellationToken = cancellationToken
            });

            foreach (var playlist in playlists)
            {
                // Queue individual playlist sync jobs
                _backgroundJobClient.Enqueue<PlaylistSyncJob>(job => 
                    job.ExecuteAsync(playlist.Id, CancellationToken.None));
            }

            _logger.LogInformation("Queued sync jobs for {Count} playlists", playlists.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error queuing playlist sync jobs");
            throw;
        }
    }
}
