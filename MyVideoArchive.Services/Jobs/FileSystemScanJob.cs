using MyVideoArchive.Models;

namespace MyVideoArchive.Services.Jobs;

/// <summary>
/// Scans the configured downloads folder for video files not yet tracked in the database.
/// Non-Custom platforms use a convention-based path check (OutputPath/{ChannelId}/{VideoId}).
/// Custom platform channels use file enumeration inside OutputPath/_Custom/{ChannelId}/.
/// Also picks up any files in "_extras" subfolders and registers them as AdditionalContentItems.
/// </summary>
public class FileSystemScanJob
{
    private static readonly string[] VideoExtensions = [".mp4", ".mkv", ".webm", ".avi", ".mov", ".flv", ".m4v", ".wmv"];

    private readonly IRepository<Channel> channelRepository;
    private readonly IConfiguration configuration;
    private readonly ILogger<FileSystemScanJob> logger;
    private readonly IRepository<Video> videoRepository;
    private readonly IRepository<AdditionalContentItem> additionalContentRepository;
    private readonly IRepository<Playlist> playlistRepository;

    public FileSystemScanJob(
        ILogger<FileSystemScanJob> logger,
        IConfiguration configuration,
        IRepository<Channel> channelRepository,
        IRepository<Video> videoRepository,
        IRepository<AdditionalContentItem> additionalContentRepository,
        IRepository<Playlist> playlistRepository)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.channelRepository = channelRepository;
        this.videoRepository = videoRepository;
        this.additionalContentRepository = additionalContentRepository;
        this.playlistRepository = playlistRepository;
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
                    await ProcessCustomChannelAsync(channel, customBasePath, result, cancellationToken);
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

    // ── Non-Custom channels ───────────────────────────────────────────────────
    // Videos are expected at OutputPath/{ChannelId}/{VideoId}.ext
    // No file enumeration - the DB is the source of truth for what videos exist.

    private static IEnumerable<string> EnumerateVideoFiles(string rootPath) =>
        Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories)
            .Where(f => VideoExtensions.Contains(
                Path.GetExtension(f),
                StringComparer.OrdinalIgnoreCase));

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
            x => new VideoEntry(x.Id, x.VideoId, x.FilePath, x.NeedsMetadataReview, x.IsManuallyImported, x.Title, x.DownloadedAt));

        var trackedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                    trackedPaths.Add(entry.FilePath);
                }
            }
        }

        if (!Directory.Exists(channelPath))
        {
            return;
        }

        foreach (string filePath in EnumerateVideoFiles(channelPath))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (trackedPaths.Contains(filePath))
            {
                continue;
            }

            try
            {
                await ProcessCustomFileAsync(filePath, channel, videosByVideoId, result, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error processing file: {FilePath}", filePath);
            }
        }
    }

    // ── Custom channels ───────────────────────────────────────────────────────
    // Files live under OutputPath/_Custom/{ChannelId}/.
    // We enumerate files and match them against existing DB records by filename.
    private async Task ProcessCustomFileAsync(
        string filePath,
        ChannelEntry channel,
        Dictionary<string, VideoEntry> videosByVideoId,
        FileSystemScanResult result,
        CancellationToken cancellationToken)
    {
        string videoId = Path.GetFileNameWithoutExtension(filePath);

        if (videosByVideoId.TryGetValue(videoId, out var existingEntry))
        {
            // Video already in DB but FilePath not set or wrong
            await LinkFoundFileAsync(existingEntry, filePath, result, cancellationToken);
            return;
        }

        // Entirely new file - create a video record
        var fileInfo = new FileInfo(filePath);
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
            NeedsMetadataReview = false
        };

        await videoRepository.InsertAsync(video, ContextOptions.ForCancellationToken(cancellationToken));
        result.NewVideos++;

        logger.LogInformation("Imported custom file {FilePath}", filePath);
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
            x => new VideoEntry(x.Id, x.VideoId, x.FilePath, x.NeedsMetadataReview, x.IsManuallyImported, x.Title, x.DownloadedAt));

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
    // Enumerate the "_extras" subfolder for files not yet in AdditionalContent.
    private async Task ScanExtrasAsync(
        ChannelEntry channel,
        string channelPath,
        CancellationToken cancellationToken)
    {
        string extrasRoot = Path.Combine(channelPath, "_extras");
        if (!Directory.Exists(extrasRoot))
        {
            return;
        }

        // Load playlists for this channel so we can resolve PlaylistId from folder name
        var playlists = await playlistRepository.FindAsync(
            new SearchOptions<Playlist>
            {
                CancellationToken = cancellationToken,
                Query = x => x.ChannelId == channel.Id
            },
            x => new { x.Id, x.PlaylistId });

        var playlistByStringId = playlists.ToDictionary(
            p => p.PlaylistId,
            p => p.Id,
            StringComparer.OrdinalIgnoreCase);

        // Load already-tracked paths to skip them
        var trackedPaths = (await additionalContentRepository.FindAsync(
            new SearchOptions<AdditionalContentItem>
            {
                CancellationToken = cancellationToken,
                Query = x => x.ChannelId == channel.Id
            },
            x => x.FilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string filePath in Directory.EnumerateFiles(extrasRoot, "*", SearchOption.AllDirectories))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (trackedPaths.Contains(filePath))
            {
                continue;
            }

            try
            {
                // Resolve PlaylistId from the immediate parent folder name if it sits one level
                // deeper than _extras (i.e. _extras/{playlistStringId}/file.pdf).
                int? playlistDbId = null;
                string? parentDir = Path.GetDirectoryName(filePath);
                if (parentDir != null && !parentDir.Equals(extrasRoot, StringComparison.OrdinalIgnoreCase))
                {
                    string folderName = Path.GetFileName(parentDir);
                    if (playlistByStringId.TryGetValue(folderName, out int dbId))
                    {
                        playlistDbId = dbId;
                    }
                }

                var fileInfo = new FileInfo(filePath);
                string ext = fileInfo.Extension.ToLowerInvariant();
                string contentType = ext switch
                {
                    ".pdf" => "application/pdf",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".gif" => "image/gif",
                    ".webp" => "image/webp",
                    ".mp3" => "audio/mpeg",
                    ".zip" => "application/zip",
                    ".txt" => "text/plain",
                    ".srt" => "text/plain",
                    ".vtt" => "text/vtt",
                    ".epub" => "application/epub+zip",
                    _ => "application/octet-stream"
                };

                var item = new AdditionalContentItem
                {
                    FileName = fileInfo.Name,
                    FilePath = filePath,
                    ContentType = contentType,
                    FileSize = fileInfo.Length,
                    UploadedAt = fileInfo.LastWriteTimeUtc,
                    ChannelId = channel.Id,
                    PlaylistId = playlistDbId,
                    VideoId = null
                };

                await additionalContentRepository.InsertAsync(item, ContextOptions.ForCancellationToken(cancellationToken));
                trackedPaths.Add(filePath);

                logger.LogInformation("Imported additional content file {FilePath}", filePath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error importing extras file: {FilePath}", filePath);
            }
        }
    }

    private record ChannelEntry(int Id, string ChannelId, string Name, string Platform);
    private record VideoEntry(int Id, string VideoId, string? FilePath, bool NeedsMetadataReview, bool IsManuallyImported, string Title, DateTime? DownloadedAt);
}

public class FileSystemScanResult
{
    public int FlaggedForReview { get; set; }
    public int MissingFiles { get; set; }
    public int NewVideos { get; set; }
    public int UpdatedVideos { get; set; }
}