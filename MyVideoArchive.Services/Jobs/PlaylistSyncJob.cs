using Hangfire;

namespace MyVideoArchive.Services.Jobs;

/// <summary>
/// Hangfire job for synchronizing playlists with their source platforms
/// </summary>
public class PlaylistSyncJob
{
    private readonly ILogger<PlaylistSyncJob> logger;
    private readonly IConfiguration configuration;
    private readonly VideoMetadataProviderFactory metadataProviderFactory;
    private readonly ThumbnailService thumbnailService;
    private readonly IBackgroundJobClient backgroundJobClient;
    private readonly IRepository<Playlist> playlistRepository;
    private readonly IRepository<PlaylistVideo> playlistVideoRepository;
    private readonly IRepository<UserPlaylist> userPlaylistRepository;
    private readonly IRepository<Video> videoRepository;

    public PlaylistSyncJob(
        ILogger<PlaylistSyncJob> logger,
        IConfiguration configuration,
        VideoMetadataProviderFactory metadataProviderFactory,
        ThumbnailService thumbnailService,
        IBackgroundJobClient backgroundJobClient,
        IRepository<Playlist> playlistRepository,
        IRepository<PlaylistVideo> playlistVideoRepository,
        IRepository<UserPlaylist> userPlaylistRepository,
        IRepository<Video> videoRepository)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.metadataProviderFactory = metadataProviderFactory;
        this.thumbnailService = thumbnailService;
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
                    .Include(x => x.Channel)
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

            string downloadPath = configuration.GetValue<string>("VideoDownload:OutputPath")
                ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

            string channelDirId = playlist.Channel?.ChannelId ?? playlist.ChannelId.ToString();
            string videoThumbnailDir = Path.Combine(downloadPath, channelDirId);

            // Update playlist metadata
            var playlistMetadata = await provider.GetPlaylistMetadataAsync(playlist.Url, cancellationToken);
            if (playlistMetadata is not null)
            {
                playlist.Name = playlistMetadata.Name;
                playlist.Description = playlistMetadata.Description;

                string playlistThumbnailDir = Path.Combine(downloadPath, channelDirId, "Playlists");
                if (!ThumbnailService.IsLocalUrl(playlist.ThumbnailUrl) && !string.IsNullOrWhiteSpace(playlistMetadata.ThumbnailUrl))
                {
                    string? localUrl = await thumbnailService.DownloadAndSaveAsync(
                        playlistMetadata.ThumbnailUrl, playlistThumbnailDir, playlist.PlaylistId,
                        downloadPath, cancellationToken);
                    // Do not overwrite an existing DB thumbnail with null when the provider can't supply one.
                    if (!string.IsNullOrWhiteSpace(localUrl))
                    {
                        playlist.ThumbnailUrl = localUrl;
                    }
                    else if (!string.IsNullOrWhiteSpace(playlistMetadata.ThumbnailUrl))
                    {
                        playlist.ThumbnailUrl = playlistMetadata.ThumbnailUrl;
                    }
                }
            }

            bool hasAnySubs = await userPlaylistRepository.ExistsAsync(
                x => x.PlaylistId == playlistId,
                ContextOptions.ForCancellationToken(cancellationToken));

            if (hasAnySubs)
            {
                // Get all videos from the playlist (deduplicate by Platform+VideoId to avoid duplicate key on insert)
                var rawList = await provider.GetPlaylistVideosAsync(playlist.Url, cancellationToken);
                var seenKeys = new HashSet<(string Platform, string VideoId)>();
                var videoMetadataList = rawList.Where(v => seenKeys.Add((v.Platform, v.VideoId))).ToList();
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
                // New videos to link to playlist after insert (Id is unknown until SaveChanges)
                var newVideoOrders = new List<(Video Video, int Order)>();

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

                    if (existingVideo is not null)
                    {
                        // We don't want to overwrite metadata for videos that have been deleted or made private....
                        if (videoMetadata.Title is not Constants.DeletedVideoTitle and
                            not Constants.PrivateVideoTitle)
                        {
                            // Update existing video metadata
                            existingVideo.Title = videoMetadata.Title;
                            existingVideo.Description = videoMetadata.Description;
                            existingVideo.Duration = videoMetadata.Duration;
                            existingVideo.ViewCount = videoMetadata.ViewCount;
                            existingVideo.LikeCount = videoMetadata.LikeCount;

                            if (videoMetadata.Title == Constants.PrivateVideoTitle)
                            {
                                existingVideo.NeedsMetadataReview = true;
                            }

                            // Keep the original remote URL until the video is downloaded.
                            // If the thumbnail already points to a local /archive/… file, leave it as-is.
                            if (!ThumbnailService.IsLocalUrl(existingVideo.ThumbnailUrl))
                            {
                                existingVideo.ThumbnailUrl = videoMetadata.ThumbnailUrl;
                            }

                            videoUpdates.Add(existingVideo);
                        }

                        // Associate existing video with this playlist if not already associated
                        if (!existingPlaylistVideoIds.Contains(existingVideo.Id))
                        {
                            if (!await playlistVideoRepository.ExistsAsync(
                                x =>
                                    x.PlaylistId == playlistId &&
                                    x.VideoId == existingVideo.Id,
                                ContextOptions.ForCancellationToken(cancellationToken)))
                            {
                                playlistVideoInserts.Add(new PlaylistVideo
                                {
                                    PlaylistId = playlistId,
                                    VideoId = existingVideo.Id,
                                    Order = videoOrder
                                });
                            }
                        }
                    }
                    else
                    {
                        // Create new video entry (without auto-downloading).
                        // Store original remote thumbnail URL — it will be replaced with a local
                        // /archive/… URL when the video is actually downloaded.
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
                            IsQueued = false,
                            NeedsMetadataReview = videoMetadata.Title == Constants.PrivateVideoTitle
                        };

                        videoInserts.Add(newVideo);
                        newVideoOrders.Add((newVideo, videoOrder));
                        newVideosCount++;
                    }
                }

                try
                {
                    await videoRepository.InsertAsync(videoInserts, ContextOptions.ForCancellationToken(cancellationToken));
                    await videoRepository.UpdateAsync(videoUpdates, ContextOptions.ForCancellationToken(cancellationToken));

                    foreach (var (insertedVideo, order) in newVideoOrders)
                    {
                        if (!await playlistVideoRepository.ExistsAsync(
                            x => x.PlaylistId == playlistId && x.VideoId == insertedVideo.Id,
                            ContextOptions.ForCancellationToken(cancellationToken)))
                        {
                            playlistVideoInserts.Add(new PlaylistVideo
                            {
                                PlaylistId = playlistId,
                                VideoId = insertedVideo.Id,
                                Order = order
                            });
                        }
                    }
                }
                catch (DbUpdateException ex) when (ex.InnerException?.Message?.Contains("23505", StringComparison.Ordinal) == true)
                {
                    // Duplicate key (Platform, VideoId): video was inserted by another sync or appears twice in the playlist
                    if (logger.IsEnabled(LogLevel.Warning))
                    {
                        logger.LogWarning(ex, "Duplicate key during playlist sync {PlaylistId}, resolving existing videos and retrying", playlistId);
                    }

                    await ResolveDuplicateVideosAndLinkAsync(
                        playlistId,
                        newVideoOrders,
                        videoUpdates,
                        playlistVideoInserts,
                        existingPlaylistVideoIds,
                        cancellationToken);
                }

                await playlistVideoRepository.InsertAsync(playlistVideoInserts, ContextOptions.ForCancellationToken(cancellationToken));

                playlist.VideoCount = await playlistVideoRepository.CountAsync(
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

    /// <summary>
    /// When bulk insert fails with duplicate key (23505), resolve each video: update and link if it exists, otherwise insert once per (Platform, VideoId).
    /// </summary>
    private async Task ResolveDuplicateVideosAndLinkAsync(
        int playlistId,
        List<(Video Video, int Order)> newVideoOrders,
        List<Video> videoUpdates,
        List<PlaylistVideo> playlistVideoInserts,
        HashSet<int> existingPlaylistVideoIds,
        CancellationToken cancellationToken)
    {
        var existingIdsAddedToUpdates = new HashSet<int>();
        var linkedVideoIds = new HashSet<int>(playlistVideoInserts.Select(pv => pv.VideoId));
        var insertedByKey = new HashSet<(string Platform, string VideoId)>();

        foreach (var (video, order) in newVideoOrders)
        {
            var key = (video.Platform, video.VideoId);
            var existingVideo = await videoRepository.FindOneAsync(new SearchOptions<Video>
            {
                CancellationToken = cancellationToken,
                Query = x => x.Platform == video.Platform && x.VideoId == video.VideoId
            });

            if (existingVideo is not null)
            {
                // We don't want to overwrite metadata for videos that have been deleted or made private.
                if (video.Title is not Constants.DeletedVideoTitle and not Constants.PrivateVideoTitle)
                {
                    existingVideo.Title = video.Title;
                    existingVideo.Description = video.Description;
                    existingVideo.Duration = video.Duration;
                    existingVideo.ViewCount = video.ViewCount;
                    existingVideo.LikeCount = video.LikeCount;

                    if (video.Title == Constants.PrivateVideoTitle)
                    {
                        existingVideo.NeedsMetadataReview = true;
                    }

                    if (!ThumbnailService.IsLocalUrl(existingVideo.ThumbnailUrl))
                    {
                        existingVideo.ThumbnailUrl = video.ThumbnailUrl;
                    }
                }

                if (existingIdsAddedToUpdates.Add(existingVideo.Id))
                {
                    videoUpdates.Add(existingVideo);
                }

                if (!existingPlaylistVideoIds.Contains(existingVideo.Id) && !linkedVideoIds.Contains(existingVideo.Id))
                {
                    linkedVideoIds.Add(existingVideo.Id);
                    playlistVideoInserts.Add(new PlaylistVideo
                    {
                        PlaylistId = playlistId,
                        VideoId = existingVideo.Id,
                        Order = order
                    });
                }
            }
            else if (insertedByKey.Add(key))
            {
                var freshVideo = new Video
                {
                    VideoId = video.VideoId,
                    Title = video.Title,
                    Description = video.Description,
                    Url = video.Url,
                    ThumbnailUrl = video.ThumbnailUrl,
                    Platform = video.Platform,
                    Duration = video.Duration,
                    UploadDate = video.UploadDate,
                    ViewCount = video.ViewCount,
                    LikeCount = video.LikeCount,
                    ChannelId = video.ChannelId,
                    IsIgnored = false,
                    IsQueued = false,
                    NeedsMetadataReview = video.NeedsMetadataReview
                };
                await videoRepository.InsertAsync(freshVideo, ContextOptions.ForCancellationToken(cancellationToken));
                playlistVideoInserts.Add(new PlaylistVideo
                {
                    PlaylistId = playlistId,
                    VideoId = freshVideo.Id,
                    Order = order
                });
            }
        }

        await videoRepository.UpdateAsync(videoUpdates, ContextOptions.ForCancellationToken(cancellationToken));
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