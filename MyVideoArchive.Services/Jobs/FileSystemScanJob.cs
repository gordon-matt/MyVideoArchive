using MyVideoArchive.Models;

namespace MyVideoArchive.Services.Jobs;

/// <summary>
/// Scans the configured downloads folder for video files not yet tracked in the database.
/// Non-Custom platforms use a convention-based path check (OutputPath/{ChannelId}/{VideoId}).
/// Custom platform channels use file enumeration inside OutputPath/_Custom/{ChannelId}/.
/// When scanning all channels, immediate subfolders of OutputPath/_Custom that are not yet
/// registered create new Custom <c>Channel</c> rows (folder name = channel id and name).
/// Also picks up any files in "_extras" subfolders and registers them as AdditionalContentItems.
/// Converts any .srt subtitle files found in custom channel folders to .vtt format.
/// Generates thumbnails for custom-platform videos that have none.
/// <para>
/// When a custom channel's folder uses the two-level hierarchy below, the scan also creates
/// <see cref="Series"/>, <see cref="Playlist"/>, <see cref="SeriesPlaylist"/>, and
/// <see cref="PlaylistVideo"/> records automatically:
/// <code>
///   _Custom/{ChannelId}/
///     SeriesA/            ← becomes a Series
///       PlaylistA1/       ← becomes a Playlist linked to SeriesA
///         video.mp4
///       PlaylistA2/       ← becomes a Playlist linked to SeriesA
///     SeriesB/            ← becomes a Series
///       PlaylistB1/       ← becomes a Playlist linked to SeriesB
/// </code>
/// If the series structure is not detected, the scan falls back to a simpler
/// playlist-only layout where each immediate subfolder becomes a standalone Playlist:
/// <code>
///   _Custom/{ChannelId}/
///     PlaylistA/          ← becomes a Playlist (no parent Series)
///       video.mp4
///     PlaylistB/          ← becomes a Playlist (no parent Series)
/// </code>
/// Non-video files found inside a playlist folder are imported as
/// <see cref="AdditionalContentItem"/>s and linked to that playlist.
/// Folders whose names begin with <c>_</c> are excluded from Series/Playlist detection.
/// </para>
/// </summary>
public class FileSystemScanJob
{
    private static readonly string[] VideoExtensions = [".mp4", ".mkv", ".webm", ".avi", ".mov", ".flv", ".m4v", ".wmv"];

    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    private static readonly string[] SubtitleExtensionsToSkip = [".vtt", ".srt"];

    private readonly IRepository<Channel> channelRepository;
    private readonly IConfiguration configuration;
    private readonly ILogger<FileSystemScanJob> logger;
    private readonly IRepository<Video> videoRepository;
    private readonly IRepository<AdditionalContentItem> additionalContentRepository;
    private readonly IRepository<Playlist> playlistRepository;
    private readonly IRepository<Series> seriesRepository;
    private readonly IRepository<SeriesPlaylist> seriesPlaylistRepository;
    private readonly IRepository<PlaylistVideo> playlistVideoRepository;
    private readonly IAdditionalContentService additionalContentService;
    private readonly ThumbnailService thumbnailService;

    public FileSystemScanJob(
        ILogger<FileSystemScanJob> logger,
        IConfiguration configuration,
        IRepository<Channel> channelRepository,
        IRepository<Video> videoRepository,
        IRepository<AdditionalContentItem> additionalContentRepository,
        IRepository<Playlist> playlistRepository,
        IRepository<Series> seriesRepository,
        IRepository<SeriesPlaylist> seriesPlaylistRepository,
        IRepository<PlaylistVideo> playlistVideoRepository,
        IAdditionalContentService additionalContentService,
        ThumbnailService thumbnailService)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.channelRepository = channelRepository;
        this.videoRepository = videoRepository;
        this.additionalContentRepository = additionalContentRepository;
        this.playlistRepository = playlistRepository;
        this.seriesRepository = seriesRepository;
        this.seriesPlaylistRepository = seriesPlaylistRepository;
        this.playlistVideoRepository = playlistVideoRepository;
        this.additionalContentService = additionalContentService;
        this.thumbnailService = thumbnailService;
    }

    /// <summary>
    /// Executes the file system scan, optionally limited to a single channel.
    /// </summary>
    /// <param name="filterChannelId">DB primary key of the channel to scan, or null to scan all channels.</param>
    /// <param name="progress">Optional progress reporter updated after each channel is processed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<FileSystemScanResult> ExecuteAsync(
        int? filterChannelId = null,
        IProgress<FileSystemScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new FileSystemScanResult();

        string downloadPath = configuration.GetValue<string>("VideoDownload:OutputPath")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

        if (!Directory.Exists(downloadPath))
        {
            logger.LogWarning("Download path does not exist: {Path}", downloadPath);
            return result;
        }

        string customBasePath = Path.Combine(downloadPath, "_Custom");

        logger.LogInformation("Starting file system scan in: {Path}", downloadPath);

        if (!filterChannelId.HasValue && Directory.Exists(customBasePath))
        {
            await DiscoverCustomChannelsFromFilesystemAsync(customBasePath, cancellationToken);
        }

        var channelOptions = new SearchOptions<Channel> { CancellationToken = cancellationToken };
        if (filterChannelId.HasValue)
        {
            channelOptions.Query = x => x.Id == filterChannelId.Value;
        }

        var channels = await channelRepository.FindAsync(
            channelOptions,
            x => new ChannelEntry(x.Id, x.ChannelId, x.Name, x.Platform));

        int totalChannels = channels.Count;
        int processedChannels = 0;

        progress?.Report(new FileSystemScanProgress { TotalChannels = totalChannels });

        foreach (var channel in channels)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                if (channel.Platform == "Custom")
                {
                    await ProcessCustomChannelAsync(channel, customBasePath, downloadPath, result, cancellationToken);
                    string customChannelPath = Path.Combine(customBasePath, channel.ChannelId);
                    await ScanExtrasAsync(channel, customChannelPath, cancellationToken);
                }
                else
                {
                    await ProcessNonCustomChannelAsync(channel, downloadPath, result, cancellationToken);
                    string channelPath = Path.Combine(downloadPath, channel.ChannelId);
                    await ScanExtrasAsync(channel, channelPath, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error processing channel {ChannelName} ({ChannelId})", channel.Name, channel.ChannelId);
            }

            processedChannels++;
            progress?.Report(new FileSystemScanProgress
            {
                TotalChannels = totalChannels,
                ProcessedChannels = processedChannels,
                CurrentChannelName = channel.Name,
                NewVideos = result.NewVideos,
                UpdatedVideos = result.UpdatedVideos,
                FlaggedForReview = result.FlaggedForReview,
                MissingFiles = result.MissingFiles
            });
        }

        logger.LogInformation(
            "File system scan complete. New: {New}, Updated: {Updated}, Flagged: {Flagged}, Missing: {Missing}",
            result.NewVideos, result.UpdatedVideos, result.FlaggedForReview, result.MissingFiles);

        return result;
    }

    /// <summary>
    /// Creates Channel rows for immediate subfolders of <paramref name="customBasePath"/>
    /// that are not yet registered (Custom platform only). Folder name becomes both
    /// channel id and display name.
    /// </summary>
    private async Task DiscoverCustomChannelsFromFilesystemAsync(string customBasePath, CancellationToken cancellationToken)
    {
        var existingIds = (await channelRepository.FindAsync(
            new SearchOptions<Channel>
            {
                CancellationToken = cancellationToken,
                Query = x => x.Platform == "Custom"
            },
            x => x.ChannelId)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string dir in Directory.EnumerateDirectories(customBasePath).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            string folderName = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(folderName) || folderName.StartsWith("_", StringComparison.Ordinal))
            {
                continue;
            }

            if (folderName.Length > 128)
            {
                logger.LogWarning(
                    "Skipping _Custom folder: name exceeds 128 characters (ChannelId limit): {Folder}",
                    folderName);
                continue;
            }

            if (existingIds.Contains(folderName))
            {
                continue;
            }

            var channel = new Channel
            {
                ChannelId = folderName,
                Name = folderName,
                Url = $"custom://{folderName}",
                Platform = "Custom",
                SubscribedAt = DateTime.UtcNow
            };

            await channelRepository.InsertAsync(channel, ContextOptions.ForCancellationToken(cancellationToken));
            existingIds.Add(folderName);
            logger.LogInformation(
                "Discovered custom channel from folder '{FolderName}'",
                folderName);
        }
    }

    // ── Non-Custom channels ───────────────────────────────────────────────────
    // Videos are expected at OutputPath/{ChannelId}/{VideoId}.ext
    // No file enumeration - the DB is the source of truth for what videos exist.

    private static IEnumerable<string> EnumerateVideoFiles(string rootPath) =>
        Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories)
            .Where(f => VideoExtensions.Contains(
                Path.GetExtension(f),
                StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Returns true when a channel folder contains at least one immediate non-special
    /// subdirectory that itself contains non-special subdirectories, indicating the
    /// Series → Playlist → Video layout.
    /// </summary>
    private static bool HasSeriesPlaylistStructure(string channelPath) =>
        Directory.Exists(channelPath) &&
        Directory.EnumerateDirectories(channelPath)
            .Where(d => !Path.GetFileName(d).StartsWith("_", StringComparison.Ordinal))
            .Any(d => Directory.EnumerateDirectories(d)
                .Any(sd => !Path.GetFileName(sd).StartsWith("_", StringComparison.Ordinal)));

    /// <summary>
    /// Returns true when a channel folder contains at least one immediate non-special
    /// subdirectory that holds video files directly (with no further non-special
    /// subdirectories), indicating a Playlist-only layout with no parent Series.
    /// </summary>
    private static bool HasPlaylistOnlyStructure(string channelPath) =>
        Directory.Exists(channelPath) &&
        Directory.EnumerateDirectories(channelPath)
            .Where(d => !Path.GetFileName(d).StartsWith("_", StringComparison.Ordinal))
            .Any(d =>
                !Directory.EnumerateDirectories(d)
                    .Any(sd => !Path.GetFileName(sd).StartsWith("_", StringComparison.Ordinal)) &&
                Directory.EnumerateFiles(d)
                    .Any(f => VideoExtensions.Contains(
                        Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)));

    private static string? FindFileByConvention(string channelPath, string videoId)
    {
        if (!Directory.Exists(channelPath))
        {
            return null;
        }

        foreach (string ext in VideoExtensions)
        {
            string candidate = Path.Combine(channelPath, videoId + ext);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task ClearMissingFileAsync(
        int videoDbId,
        string videoId,
        FileSystemScanResult result,
        CancellationToken cancellationToken)
    {
        var video = await videoRepository.FindOneAsync(new SearchOptions<Video>
        {
            CancellationToken = cancellationToken,
            Query = x => x.Id == videoDbId
        });

        if (video is null)
        {
            return;
        }

        video.FilePath = null;
        video.DownloadedAt = null;
        await videoRepository.UpdateAsync(video, ContextOptions.ForCancellationToken(cancellationToken));
        result.MissingFiles++;

        logger.LogInformation("File no longer exists, cleared path for video {VideoId}", videoId);
    }

    // ── Shared helpers ────────────────────────────────────────────────────────
    private async Task LinkFoundFileAsync(
        VideoEntry entry,
        string filePath,
        FileSystemScanResult result,
        CancellationToken cancellationToken)
    {
        var video = await videoRepository.FindOneAsync(new SearchOptions<Video>
        {
            CancellationToken = cancellationToken,
            Query = x => x.Id == entry.Id
        });

        if (video is null)
        {
            return;
        }

        video.FilePath = filePath;
        video.FileSize = new FileInfo(filePath).Length;
        video.DownloadFailed = false;
        video.DownloadedAt ??= File.GetLastWriteTimeUtc(filePath);

        if (video.NeedsMetadataReview &&
            !string.IsNullOrEmpty(video.Title) &&
            !string.Equals(video.Title, video.VideoId, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(video.Title, Path.GetFileNameWithoutExtension(filePath), StringComparison.OrdinalIgnoreCase))
        {
            video.NeedsMetadataReview = false;
            video.IsManuallyImported = false;
        }

        await videoRepository.UpdateAsync(video, ContextOptions.ForCancellationToken(cancellationToken));
        result.UpdatedVideos++;

        logger.LogInformation("Linked file {FilePath} to video {VideoId}", filePath, entry.VideoId);
    }

    private async Task ProcessCustomChannelAsync(
        ChannelEntry channel,
        string customBasePath,
        string downloadBasePath,
        FileSystemScanResult result,
        CancellationToken cancellationToken)
    {
        string channelPath = Path.Combine(customBasePath, channel.ChannelId);

        var videoEntries = await videoRepository.FindAsync(
            new SearchOptions<Video>
            {
                CancellationToken = cancellationToken,
                Query = x => x.ChannelId == channel.Id
            },
            x => new VideoEntry(x.Id, x.VideoId, x.FilePath, x.NeedsMetadataReview, x.IsManuallyImported, x.Title, x.DownloadedAt, x.ThumbnailUrl));

        var trackedVideoPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var videosByVideoId = new Dictionary<string, VideoEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in videoEntries)
        {
            videosByVideoId[entry.VideoId] = entry;

            if (entry.FilePath != null)
            {
                if (!File.Exists(entry.FilePath))
                {
                    await ClearMissingFileAsync(entry.Id, entry.VideoId, result, cancellationToken);
                }
                else
                {
                    trackedVideoPaths.Add(entry.FilePath);
                }
            }
        }

        if (!Directory.Exists(channelPath))
        {
            return;
        }

        // Convert any .srt subtitle files found in the channel folder to .vtt
        ConvertSrtFilesInDirectory(channelPath);

        var allVideoFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string filePath in EnumerateVideoFiles(channelPath))
        {
            allVideoFilePaths.Add(filePath);

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (trackedVideoPaths.Contains(filePath))
            {
                // For already-tracked videos with no thumbnail, try to resolve one
                string videoId = Path.GetFileNameWithoutExtension(filePath);
                if (videosByVideoId.TryGetValue(videoId, out var existingEntry) && string.IsNullOrEmpty(existingEntry.ThumbnailUrl))
                {
                    await TryResolveThumbnailAsync(existingEntry.Id, filePath, downloadBasePath, cancellationToken);
                }
                continue;
            }

            try
            {
                await ProcessCustomFileAsync(filePath, channel, videosByVideoId, downloadBasePath, result, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error processing file: {FilePath}", filePath);
            }
        }

        // When the folder layout follows the Series → Playlist → Video convention,
        // create/update Series, Playlist, and PlaylistVideo records accordingly.
        // If the series structure is not detected, try the simpler Playlist-only layout.
        // The returned dictionary maps each playlist folder path to its DB playlist ID
        // so that the extras scan can assign content to the right playlist.
        IReadOnlyDictionary<string, int>? playlistFolderPaths = null;
        if (HasSeriesPlaylistStructure(channelPath))
        {
            playlistFolderPaths = await ScanCustomChannelSeriesStructureAsync(channel, channelPath, cancellationToken);
        }
        else if (HasPlaylistOnlyStructure(channelPath))
        {
            playlistFolderPaths = await ScanCustomChannelPlaylistOnlyStructureAsync(channel, channelPath, cancellationToken);
        }

        // Scan non-video files in the channel root as additional content
        await ScanCustomChannelRootExtrasAsync(channel, channelPath, allVideoFilePaths, playlistFolderPaths, cancellationToken);
    }

    // ── Custom channels ───────────────────────────────────────────────────────
    // Files live under OutputPath/_Custom/{ChannelId}/.
    // We enumerate files and match them against existing DB records by filename.
    private async Task ProcessCustomFileAsync(
        string filePath,
        ChannelEntry channel,
        Dictionary<string, VideoEntry> videosByVideoId,
        string downloadBasePath,
        FileSystemScanResult result,
        CancellationToken cancellationToken)
    {
        string videoId = Path.GetFileNameWithoutExtension(filePath);

        if (videosByVideoId.TryGetValue(videoId, out var existingEntry))
        {
            await LinkFoundFileAsync(existingEntry, filePath, result, cancellationToken);
            return;
        }

        var fileInfo = new FileInfo(filePath);
        string? thumbnailUrl = FindSidecardThumbnail(filePath, downloadBasePath);

        var video = new Video
        {
            VideoId = videoId,
            Title = videoId,
            Url = filePath,
            Platform = "Custom",
            UploadDate = fileInfo.LastWriteTimeUtc,
            FilePath = filePath,
            FileSize = fileInfo.Length,
            DownloadedAt = fileInfo.LastWriteTimeUtc,
            ChannelId = channel.Id,
            IsManuallyImported = true,
            NeedsMetadataReview = false,
            ThumbnailUrl = thumbnailUrl
        };

        await videoRepository.InsertAsync(video, ContextOptions.ForCancellationToken(cancellationToken));
        result.NewVideos++;

        logger.LogInformation("Imported custom file {FilePath}", filePath);

        // Generate thumbnail via FFmpeg if no sidecar image was found
        if (string.IsNullOrEmpty(thumbnailUrl))
        {
            await TryResolveThumbnailAsync(video.Id, filePath, downloadBasePath, cancellationToken);
        }
    }

    /// <summary>
    /// Inspects the two-level subfolder hierarchy beneath <paramref name="channelPath"/>
    /// and ensures that Series, Playlist, SeriesPlaylist, and PlaylistVideo records exist for it.
    /// <para>
    /// Layout convention:
    /// <code>
    ///   channelPath/
    ///     SeriesA/          ← Series
    ///       PlaylistA1/     ← Playlist (child of SeriesA)
    ///         video.mp4
    ///       PlaylistA2/     ← Playlist (child of SeriesA)
    ///     SeriesB/          ← Series
    ///       PlaylistB1/     ← Playlist (child of SeriesB)
    /// </code>
    /// </para>
    /// </summary>
    /// <returns>
    /// A dictionary mapping each level-2 folder path to the playlist DB primary key,
    /// used by the extras scan to assign additional content to the correct playlist.
    /// </returns>
    private async Task<IReadOnlyDictionary<string, int>> ScanCustomChannelSeriesStructureAsync(
        ChannelEntry channel,
        string channelPath,
        CancellationToken cancellationToken)
    {
        var playlistFolderPaths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // ── Load existing series and playlists for this channel ───────────────

        var existingSeries = await seriesRepository.FindAsync(
            new SearchOptions<Series> { CancellationToken = cancellationToken, Query = x => x.ChannelId == channel.Id },
            x => new SeriesEntry(x.Id, x.Name));

        var seriesByName = existingSeries.ToDictionary(s => s.Name, s => s.Id, StringComparer.OrdinalIgnoreCase);

        var existingPlaylists = await playlistRepository.FindAsync(
            new SearchOptions<Playlist> { CancellationToken = cancellationToken, Query = x => x.ChannelId == channel.Id },
            x => new PlaylistEntry(x.Id, x.PlaylistId));

        var playlistByPlaylistId = existingPlaylists.ToDictionary(p => p.PlaylistId, p => p.Id, StringComparer.OrdinalIgnoreCase);

        // ── Load existing series↔playlist and playlist↔video links ────────────

        var allSeriesIds = seriesByName.Values.ToHashSet();

        var existingSeriesPlaylistLinks = allSeriesIds.Count > 0
            ? (await seriesPlaylistRepository.FindAsync(
                new SearchOptions<SeriesPlaylist>
                {
                    CancellationToken = cancellationToken,
                    Query = x => allSeriesIds.Contains(x.SeriesId)
                },
                x => new { x.SeriesId, x.PlaylistId }))
                .Select(sp => (sp.SeriesId, sp.PlaylistId))
                .ToHashSet()
            : [];

        var allPlaylistIds = playlistByPlaylistId.Values.ToHashSet();

        var existingPlaylistVideoKeys = allPlaylistIds.Count > 0
            ? (await playlistVideoRepository.FindAsync(
                new SearchOptions<PlaylistVideo>
                {
                    CancellationToken = cancellationToken,
                    Query = x => allPlaylistIds.Contains(x.PlaylistId)
                },
                x => new { x.PlaylistId, x.VideoId }))
                .Select(pv => (pv.PlaylistId, pv.VideoId))
                .ToHashSet()
            : [];

        // ── Load channel videos keyed by file path (includes newly inserted ones) ──

        var videosByFilePath = (await videoRepository.FindAsync(
            new SearchOptions<Video> { CancellationToken = cancellationToken, Query = x => x.ChannelId == channel.Id },
            x => new { x.Id, x.FilePath }))
            .Where(v => v.FilePath != null)
            .ToDictionary(v => v.FilePath!, v => v.Id, StringComparer.OrdinalIgnoreCase);

        // ── Running order counters ────────────────────────────────────────────

        var seriesPlaylistOrder = existingSeriesPlaylistLinks
            .GroupBy(k => k.SeriesId)
            .ToDictionary(g => g.Key, g => g.Count());

        var playlistVideoOrder = existingPlaylistVideoKeys
            .GroupBy(k => k.PlaylistId)
            .ToDictionary(g => g.Key, g => g.Count());

        // ── Iterate level-1 directories (Series) ─────────────────────────────

        foreach (string levelOneDir in Directory.EnumerateDirectories(channelPath).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            string seriesName = Path.GetFileName(levelOneDir);

            if (seriesName.StartsWith("_", StringComparison.Ordinal))
            {
                continue;
            }

            var levelTwoDirs = Directory.EnumerateDirectories(levelOneDir)
                .Where(d => !Path.GetFileName(d).StartsWith("_", StringComparison.Ordinal))
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (levelTwoDirs.Count == 0)
            {
                continue;
            }

            // ── Find or create Series ─────────────────────────────────────────

            if (!seriesByName.TryGetValue(seriesName, out int seriesDbId))
            {
                var newSeries = new Series { Name = seriesName, ChannelId = channel.Id };
                await seriesRepository.InsertAsync(newSeries, ContextOptions.ForCancellationToken(cancellationToken));
                seriesDbId = newSeries.Id;
                seriesByName[seriesName] = seriesDbId;
                allSeriesIds.Add(seriesDbId);
                logger.LogInformation("Created series '{SeriesName}' for channel {ChannelId}", seriesName, channel.ChannelId);
            }

            // ── Iterate level-2 directories (Playlists) ───────────────────────

            foreach (string levelTwoDir in levelTwoDirs)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                string playlistFolderName = Path.GetFileName(levelTwoDir);

                // PlaylistId includes both the channel and series name so that
                // identically-named playlist folders in different series don't collide.
                string playlistId = $"{channel.ChannelId}/{seriesName}/{playlistFolderName}";

                // ── Find or create Playlist ───────────────────────────────────

                if (!playlistByPlaylistId.TryGetValue(playlistId, out int playlistDbId))
                {
                    var newPlaylist = new Playlist
                    {
                        PlaylistId = playlistId,
                        Name = playlistFolderName,
                        Url = levelTwoDir,
                        Platform = "Custom",
                        ChannelId = channel.Id,
                        SubscribedAt = DateTime.UtcNow
                    };
                    await playlistRepository.InsertAsync(newPlaylist, ContextOptions.ForCancellationToken(cancellationToken));
                    playlistDbId = newPlaylist.Id;
                    playlistByPlaylistId[playlistId] = playlistDbId;
                    allPlaylistIds.Add(playlistDbId);
                    playlistVideoOrder[playlistDbId] = 0;
                    logger.LogInformation(
                        "Created playlist '{PlaylistName}' under series '{SeriesName}' for channel {ChannelId}",
                        playlistFolderName, seriesName, channel.ChannelId);
                }

                playlistFolderPaths[levelTwoDir] = playlistDbId;

                // ── Ensure Series ↔ Playlist link ─────────────────────────────

                var seriesPlaylistKey = (SeriesId: seriesDbId, PlaylistId: playlistDbId);
                if (!existingSeriesPlaylistLinks.Contains(seriesPlaylistKey))
                {
                    int spOrder = seriesPlaylistOrder.GetValueOrDefault(seriesDbId, 0);
                    await seriesPlaylistRepository.InsertAsync(
                        new SeriesPlaylist { SeriesId = seriesDbId, PlaylistId = playlistDbId, SortOrder = spOrder },
                        ContextOptions.ForCancellationToken(cancellationToken));
                    existingSeriesPlaylistLinks.Add(seriesPlaylistKey);
                    seriesPlaylistOrder[seriesDbId] = spOrder + 1;
                    logger.LogInformation(
                        "Linked playlist '{PlaylistName}' to series '{SeriesName}'",
                        playlistFolderName, seriesName);
                }

                // ── Link videos found in this folder to the playlist ──────────

                foreach (string videoFilePath in EnumerateVideoFiles(levelTwoDir))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (!videosByFilePath.TryGetValue(videoFilePath, out int videoDbId))
                    {
                        continue;
                    }

                    var pvKey = (PlaylistId: playlistDbId, VideoId: videoDbId);
                    if (!existingPlaylistVideoKeys.Contains(pvKey))
                    {
                        int order = playlistVideoOrder.GetValueOrDefault(playlistDbId, 0);
                        await playlistVideoRepository.InsertAsync(
                            new PlaylistVideo { PlaylistId = playlistDbId, VideoId = videoDbId, Order = order },
                            ContextOptions.ForCancellationToken(cancellationToken));
                        existingPlaylistVideoKeys.Add(pvKey);
                        playlistVideoOrder[playlistDbId] = order + 1;
                    }
                }
            }
        }

        return playlistFolderPaths;
    }

    /// <summary>
    /// Inspects the one-level subfolder hierarchy beneath <paramref name="channelPath"/>
    /// and ensures that Playlist and PlaylistVideo records exist for it (no Series).
    /// <para>
    /// Layout convention:
    /// <code>
    ///   channelPath/
    ///     PlaylistA/     ← Playlist (no parent Series)
    ///       video.mp4
    ///     PlaylistB/     ← Playlist (no parent Series)
    /// </code>
    /// </para>
    /// </summary>
    /// <returns>
    /// A dictionary mapping each playlist folder path to the playlist DB primary key,
    /// used by the extras scan to assign additional content to the correct playlist.
    /// </returns>
    private async Task<IReadOnlyDictionary<string, int>> ScanCustomChannelPlaylistOnlyStructureAsync(
        ChannelEntry channel,
        string channelPath,
        CancellationToken cancellationToken)
    {
        var playlistFolderPaths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var existingPlaylists = await playlistRepository.FindAsync(
            new SearchOptions<Playlist> { CancellationToken = cancellationToken, Query = x => x.ChannelId == channel.Id },
            x => new PlaylistEntry(x.Id, x.PlaylistId));

        var playlistByPlaylistId = existingPlaylists.ToDictionary(p => p.PlaylistId, p => p.Id, StringComparer.OrdinalIgnoreCase);

        var allPlaylistIds = playlistByPlaylistId.Values.ToHashSet();

        var existingPlaylistVideoKeys = allPlaylistIds.Count > 0
            ? (await playlistVideoRepository.FindAsync(
                new SearchOptions<PlaylistVideo>
                {
                    CancellationToken = cancellationToken,
                    Query = x => allPlaylistIds.Contains(x.PlaylistId)
                },
                x => new { x.PlaylistId, x.VideoId }))
                .Select(pv => (pv.PlaylistId, pv.VideoId))
                .ToHashSet()
            : [];

        var videosByFilePath = (await videoRepository.FindAsync(
            new SearchOptions<Video> { CancellationToken = cancellationToken, Query = x => x.ChannelId == channel.Id },
            x => new { x.Id, x.FilePath }))
            .Where(v => v.FilePath != null)
            .ToDictionary(v => v.FilePath!, v => v.Id, StringComparer.OrdinalIgnoreCase);

        var playlistVideoOrder = existingPlaylistVideoKeys
            .GroupBy(k => k.PlaylistId)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (string playlistDir in Directory.EnumerateDirectories(channelPath).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            string folderName = Path.GetFileName(playlistDir);

            if (folderName.StartsWith("_", StringComparison.Ordinal))
            {
                continue;
            }

            // Skip folders that themselves contain non-special subdirectories (series structure)
            var subDirs = Directory.EnumerateDirectories(playlistDir)
                .Where(d => !Path.GetFileName(d).StartsWith("_", StringComparison.Ordinal))
                .ToList();

            if (subDirs.Count > 0)
            {
                continue;
            }

            string playlistId = $"{channel.ChannelId}/{folderName}";

            if (!playlistByPlaylistId.TryGetValue(playlistId, out int playlistDbId))
            {
                var newPlaylist = new Playlist
                {
                    PlaylistId = playlistId,
                    Name = folderName,
                    Url = playlistDir,
                    Platform = "Custom",
                    ChannelId = channel.Id,
                    SubscribedAt = DateTime.UtcNow
                };
                await playlistRepository.InsertAsync(newPlaylist, ContextOptions.ForCancellationToken(cancellationToken));
                playlistDbId = newPlaylist.Id;
                playlistByPlaylistId[playlistId] = playlistDbId;
                allPlaylistIds.Add(playlistDbId);
                playlistVideoOrder[playlistDbId] = 0;
                logger.LogInformation(
                    "Created playlist '{PlaylistName}' for channel {ChannelId}",
                    folderName, channel.ChannelId);
            }

            playlistFolderPaths[playlistDir] = playlistDbId;

            foreach (string videoFilePath in EnumerateVideoFiles(playlistDir))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (!videosByFilePath.TryGetValue(videoFilePath, out int videoDbId))
                {
                    continue;
                }

                var pvKey = (PlaylistId: playlistDbId, VideoId: videoDbId);
                if (!existingPlaylistVideoKeys.Contains(pvKey))
                {
                    int order = playlistVideoOrder.GetValueOrDefault(playlistDbId, 0);
                    await playlistVideoRepository.InsertAsync(
                        new PlaylistVideo { PlaylistId = playlistDbId, VideoId = videoDbId, Order = order },
                        ContextOptions.ForCancellationToken(cancellationToken));
                    existingPlaylistVideoKeys.Add(pvKey);
                    playlistVideoOrder[playlistDbId] = order + 1;
                }
            }
        }

        return playlistFolderPaths;
    }

    /// <summary>
    /// Looks for an image file alongside the video with the same base name.
    /// Returns a relative /archive/… URL if one is found, otherwise null.
    /// </summary>
    private static string? FindSidecardThumbnail(string videoFilePath, string downloadBasePath)
    {
        string dir = Path.GetDirectoryName(videoFilePath) ?? string.Empty;
        string baseName = Path.GetFileNameWithoutExtension(videoFilePath);

        foreach (string ext in ImageExtensions)
        {
            string candidate = Path.Combine(dir, baseName + ext);
            if (File.Exists(candidate))
            {
                return ThumbnailService.BuildRelativeUrl(downloadBasePath, candidate);
            }
        }

        return null;
    }

    /// <summary>
    /// Attempts to generate a thumbnail from the video file and update the DB record.
    /// </summary>
    private async Task TryResolveThumbnailAsync(
        int videoDbId,
        string videoFilePath,
        string downloadBasePath,
        CancellationToken cancellationToken)
    {
        try
        {
            string saveDirectory = Path.GetDirectoryName(videoFilePath) ?? downloadBasePath;
            string baseName = Path.GetFileNameWithoutExtension(videoFilePath);

            string? thumbnailUrl = await thumbnailService.GenerateFromVideoAsync(
                videoFilePath,
                saveDirectory,
                baseName,
                downloadBasePath,
                cancellationToken);

            if (string.IsNullOrEmpty(thumbnailUrl))
            {
                return;
            }

            var video = await videoRepository.FindOneAsync(new SearchOptions<Video>
            {
                CancellationToken = cancellationToken,
                Query = x => x.Id == videoDbId
            });

            if (video is not null)
            {
                video.ThumbnailUrl = thumbnailUrl;
                await videoRepository.UpdateAsync(video, ContextOptions.ForCancellationToken(cancellationToken));
                logger.LogInformation("Generated thumbnail for video {VideoId}", videoDbId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to generate thumbnail for video {VideoId}", videoDbId);
        }
    }

    /// <summary>
    /// Converts any .srt files in the given directory (and subdirectories) to .vtt.
    /// The .srt file is kept in place; the .vtt is written alongside it.
    /// </summary>
    private void ConvertSrtFilesInDirectory(string directoryPath)
    {
        try
        {
            foreach (string srtPath in Directory.EnumerateFiles(directoryPath, "*.srt", SearchOption.AllDirectories))
            {
                string vttPath = Path.ChangeExtension(srtPath, ".vtt");
                if (File.Exists(vttPath))
                {
                    continue;
                }

                try
                {
                    SubtitleConverter.ConvertFile(srtPath, vttPath);
                    logger.LogInformation("Converted subtitle {SrtPath} → {VttPath}", srtPath, vttPath);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to convert subtitle file {SrtPath}", srtPath);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error enumerating subtitle files in {Directory}", directoryPath);
        }
    }

    private async Task ProcessNonCustomChannelAsync(
        ChannelEntry channel,
        string downloadPath,
        FileSystemScanResult result,
        CancellationToken cancellationToken)
    {
        string channelPath = Path.Combine(downloadPath, channel.ChannelId);

        var videoEntries = await videoRepository.FindAsync(
            new SearchOptions<Video>
            {
                CancellationToken = cancellationToken,
                Query = x => x.ChannelId == channel.Id
            },
            x => new VideoEntry(x.Id, x.VideoId, x.FilePath, x.NeedsMetadataReview, x.IsManuallyImported, x.Title, x.DownloadedAt, x.ThumbnailUrl));

        foreach (var entry in videoEntries)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (entry.FilePath != null)
            {
                if (!File.Exists(entry.FilePath))
                {
                    await ClearMissingFileAsync(entry.Id, entry.VideoId, result, cancellationToken);
                }
            }
            else
            {
                string? found = FindFileByConvention(channelPath, entry.VideoId);
                if (found is not null)
                {
                    await LinkFoundFileAsync(entry, found, result, cancellationToken);
                }
            }
        }
    }

    // ── Extras scanning ───────────────────────────────────────────────────────
    // Enumerate every `_extras` directory under the channel (channel-level and nested,
    // e.g. next to a playlist folder) for files not yet in AdditionalContent.
    private async Task ScanExtrasAsync(
        ChannelEntry channel,
        string channelPath,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(channelPath))
        {
            return;
        }

        // Playlists: string id (legacy extras parent-name match) + filesystem Url (series/playlist scan)
        var playlists = await playlistRepository.FindAsync(
            new SearchOptions<Playlist>
            {
                CancellationToken = cancellationToken,
                Query = x => x.ChannelId == channel.Id
            },
            x => new { x.Id, x.PlaylistId, x.Url });

        var playlistByStringId = playlists.ToDictionary(
            p => p.PlaylistId,
            p => p.Id,
            StringComparer.OrdinalIgnoreCase);

        var playlistPathToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in playlists)
        {
            if (string.IsNullOrWhiteSpace(p.Url) || p.Url.Contains("://", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                string fullPl = Path.GetFullPath(p.Url);
                playlistPathToId[fullPl] = p.Id;
            }
            catch
            {
                // ignore invalid paths
            }
        }

        var videoIdToDbId = (await videoRepository.FindAsync(
            new SearchOptions<Video>
            {
                CancellationToken = cancellationToken,
                Query = x => x.ChannelId == channel.Id
            },
            x => new { x.Id, x.VideoId }))
            .ToDictionary(x => x.VideoId, x => x.Id, StringComparer.OrdinalIgnoreCase);

        var trackedPaths = (await additionalContentRepository.FindAsync(
            new SearchOptions<AdditionalContentItem>
            {
                CancellationToken = cancellationToken,
                Query = x => x.ChannelId == channel.Id
            },
            x => x.FilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var extrasDirectoryRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string channelLevelExtras = Path.Combine(channelPath, "_extras");
        if (Directory.Exists(channelLevelExtras))
        {
            extrasDirectoryRoots.Add(Path.GetFullPath(channelLevelExtras));
        }

        foreach (string dir in Directory.EnumerateDirectories(channelPath, "*", SearchOption.AllDirectories))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (Path.GetFileName(dir).Equals("_extras", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    extrasDirectoryRoots.Add(Path.GetFullPath(dir));
                }
                catch
                {
                    // ignore invalid paths
                }
            }
        }

        foreach (string extrasDir in extrasDirectoryRoots)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            foreach (string filePath in Directory.EnumerateFiles(extrasDir, "*", SearchOption.AllDirectories))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (trackedPaths.Contains(filePath))
                {
                    continue;
                }

                // Subtitle files are not treated as additional content
                string ext = Path.GetExtension(filePath);
                if (SubtitleExtensionsToSkip.Contains(ext, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    int? playlistDbId = TryResolvePlaylistForExtrasFile(filePath, channelPath, playlistPathToId);
                    if (!playlistDbId.HasValue)
                    {
                        string? parentDir = Path.GetDirectoryName(filePath);
                        if (parentDir is not null)
                        {
                            string folderName = Path.GetFileName(parentDir);
                            if (playlistByStringId.TryGetValue(folderName, out int dbId))
                            {
                                playlistDbId = dbId;
                            }
                        }
                    }

                    int? videoDbId = TryResolveVideoFromExtrasSubfolder(channelPath, filePath, videoIdToDbId);
                    await additionalContentService.ImportFileAsync(filePath, channel.Id, playlistDbId, videoDbId, cancellationToken);
                    trackedPaths.Add(filePath);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Error importing extras file: {FilePath}", filePath);
                }
            }
        }
    }

    /// <summary>
    /// Scans all non-video, non-subtitle files in a custom channel's root folder and
    /// imports any untracked ones as AdditionalContentItems.
    /// This complements ScanExtrasAsync (which only looks in _extras) so that files
    /// placed directly in the channel folder are also picked up.
    /// </summary>
    /// <param name="playlistFolderPaths">
    /// When the channel uses the Series → Playlist → Video layout, maps each playlist
    /// folder path to its database playlist ID so that content files found inside a
    /// playlist folder are automatically linked to that playlist.
    /// Pass <c>null</c> for channels with a flat structure.
    /// </param>
    private async Task ScanCustomChannelRootExtrasAsync(
        ChannelEntry channel,
        string channelPath,
        HashSet<string> videoFilePaths,
        IReadOnlyDictionary<string, int>? playlistFolderPaths,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(channelPath))
        {
            return;
        }

        // Build the set of sidecar thumbnail paths (same directory and base name as a video
        // file, image extension) so they are not imported as additional content.
        var sidecarThumbnailPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string videoPath in videoFilePaths)
        {
            string videoDir = Path.GetDirectoryName(videoPath) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(videoPath);
            foreach (string imgExt in ImageExtensions)
            {
                string candidate = Path.Combine(videoDir, baseName + imgExt);
                if (File.Exists(candidate))
                {
                    sidecarThumbnailPaths.Add(candidate);
                }
            }
        }

        var trackedPaths = (await additionalContentRepository.FindAsync(
            new SearchOptions<AdditionalContentItem>
            {
                CancellationToken = cancellationToken,
                Query = x => x.ChannelId == channel.Id
            },
            x => x.FilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string filePath in Directory.EnumerateFiles(channelPath, "*", SearchOption.AllDirectories))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (trackedPaths.Contains(filePath))
            {
                continue;
            }

            if (IsUnderAnyExtrasFolder(channelPath, filePath))
            {
                continue;
            }

            if (videoFilePaths.Contains(filePath))
            {
                continue;
            }

            if (sidecarThumbnailPaths.Contains(filePath))
            {
                continue;
            }

            string ext = Path.GetExtension(filePath);

            if (VideoExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (SubtitleExtensionsToSkip.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                int? playlistDbId = ResolvePlaylistForFile(filePath, channelPath, playlistFolderPaths);
                await additionalContentService.ImportFileAsync(filePath, channel.Id, playlistDbId, null, cancellationToken);
                trackedPaths.Add(filePath);
                logger.LogInformation("Imported additional content file {FilePath}", filePath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error importing additional content file: {FilePath}", filePath);
            }
        }
    }

    /// <summary>
    /// When the path contains a <c>_extras</c> folder (anywhere under the channel), the first segment
    /// after the <b>last</b> <c>_extras</c> is matched to <c>VideoId</c> (case-insensitive).
    /// Example: <c>…/PlaylistFolder/_extras/03 - Breakout/readme.pdf</c> → video id <c>03 - Breakout</c>.
    /// </summary>
    private static int? TryResolveVideoFromExtrasSubfolder(
        string channelPath,
        string filePath,
        IReadOnlyDictionary<string, int> videoIdToDbId)
    {
        if (videoIdToDbId.Count == 0)
        {
            return null;
        }

        try
        {
            string channelRoot = Path.GetFullPath(channelPath);
            string full = Path.GetFullPath(filePath);
            if (!full.StartsWith(channelRoot, StringComparison.OrdinalIgnoreCase) || full.Length <= channelRoot.Length)
            {
                return null;
            }

            string rel = Path.GetRelativePath(channelRoot, full);
            if (string.IsNullOrEmpty(rel) || rel == "." || rel.StartsWith("..", StringComparison.Ordinal))
            {
                return null;
            }

            string[] parts = rel.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            int extrasIdx = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Equals("_extras", StringComparison.OrdinalIgnoreCase))
                {
                    extrasIdx = i;
                }
            }

            // Need: …/_extras/{videoId}/{fileName} at minimum
            if (extrasIdx < 0 || extrasIdx + 2 >= parts.Length)
            {
                return null;
            }

            string videoFolder = parts[extrasIdx + 1];
            return videoIdToDbId.TryGetValue(videoFolder, out int id) ? id : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Walks ancestors of the file to find a folder that matches a playlist’s on-disk <c>Url</c>.
    /// </summary>
    private static int? TryResolvePlaylistForExtrasFile(
        string filePath,
        string channelPath,
        IReadOnlyDictionary<string, int> playlistPathToId)
    {
        if (playlistPathToId.Count == 0)
        {
            return null;
        }

        string? dir = Path.GetDirectoryName(filePath);
        while (dir is not null && !dir.Equals(channelPath, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string fullDir = Path.GetFullPath(dir);
                if (playlistPathToId.TryGetValue(fullDir, out int id))
                {
                    return id;
                }
            }
            catch
            {
                // ignore
            }

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    /// <summary>
    /// True when <paramref name="filePath"/> is under the channel tree and a path segment is <c>_extras</c>.
    /// </summary>
    private static bool IsUnderAnyExtrasFolder(string channelPath, string filePath)
    {
        try
        {
            string channelRoot = Path.GetFullPath(channelPath);
            string full = Path.GetFullPath(filePath);
            if (!full.StartsWith(channelRoot, StringComparison.OrdinalIgnoreCase) || full.Length <= channelRoot.Length)
            {
                return false;
            }

            string rel = Path.GetRelativePath(channelRoot, full);
            if (string.IsNullOrEmpty(rel) || rel == "." || rel.StartsWith("..", StringComparison.Ordinal))
            {
                return false;
            }

            return rel.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
                .Any(s => s.Equals("_extras", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Walks up the directory tree from a file's parent folder until it either finds a
    /// matching entry in <paramref name="playlistFolderPaths"/> or reaches the channel
    /// root, returning the associated playlist DB ID or <c>null</c>.
    /// This handles files placed directly inside a playlist folder as well as files in
    /// any subfolder of a playlist folder (e.g. a <c>_extras</c> sub-directory).
    /// </summary>
    private static int? ResolvePlaylistForFile(
        string filePath,
        string channelPath,
        IReadOnlyDictionary<string, int>? playlistFolderPaths)
    {
        if (playlistFolderPaths is null || playlistFolderPaths.Count == 0)
        {
            return null;
        }

        string? dir = Path.GetDirectoryName(filePath);

        while (dir != null && !dir.Equals(channelPath, StringComparison.OrdinalIgnoreCase))
        {
            if (playlistFolderPaths.TryGetValue(dir, out int playlistDbId))
            {
                return playlistDbId;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    private record ChannelEntry(int Id, string ChannelId, string Name, string Platform);
    private record VideoEntry(int Id, string VideoId, string? FilePath, bool NeedsMetadataReview, bool IsManuallyImported, string Title, DateTime? DownloadedAt, string? ThumbnailUrl);
    private record SeriesEntry(int Id, string Name);
    private record PlaylistEntry(int Id, string PlaylistId);
}

public class FileSystemScanResult
{
    public int FlaggedForReview { get; set; }
    public int MissingFiles { get; set; }
    public int NewVideos { get; set; }
    public int UpdatedVideos { get; set; }
}