namespace MyVideoArchive.Services.Jobs;

/// <summary>
/// Scans the configured downloads folder for video files not yet tracked in the database.
/// Attempts to fetch metadata from the source platform, falling back to file-level info.
/// </summary>
public class FileSystemScanJob
{
    private static readonly string[] VideoExtensions = [".mp4", ".mkv", ".webm", ".avi", ".mov", ".flv", ".m4v", ".wmv"];

    private readonly ILogger<FileSystemScanJob> logger;
    private readonly IConfiguration configuration;
    private readonly VideoMetadataProviderFactory metadataProviderFactory;
    private readonly ThumbnailService thumbnailService;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<Video> videoRepository;

    public FileSystemScanJob(
        ILogger<FileSystemScanJob> logger,
        IConfiguration configuration,
        VideoMetadataProviderFactory metadataProviderFactory,
        ThumbnailService thumbnailService,
        IRepository<Channel> channelRepository,
        IRepository<Video> videoRepository)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.metadataProviderFactory = metadataProviderFactory;
        this.thumbnailService = thumbnailService;
        this.channelRepository = channelRepository;
        this.videoRepository = videoRepository;
    }

    private record VideoIdEntry(string VideoId, int Id, string? FilePath);

    public async Task<FileSystemScanResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var result = new FileSystemScanResult();

        string downloadPath = configuration.GetValue<string>("VideoDownload:OutputPath")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

        if (!Directory.Exists(downloadPath))
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("Download path does not exist: {Path}", downloadPath);
            }

            return result;
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Starting file system scan in: {Path}", downloadPath);
        }

        var allVideos = await videoRepository.FindAsync(new SearchOptions<Video>
        {
            CancellationToken = cancellationToken
        }, x => new VideoIdEntry(x.VideoId, x.Id, x.FilePath));

        var existingPaths = allVideos
            .Where(v => v.FilePath != null)
            .Select(v => v.FilePath!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingVideoIds = allVideos
            .ToDictionary(v => v.VideoId, v => v, StringComparer.OrdinalIgnoreCase);

        foreach (string filePath in EnumerateVideoFiles(downloadPath))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await ProcessFileAsync(filePath, existingPaths, existingVideoIds, result, cancellationToken);
            }
            catch (Exception ex)
            {
                if (logger.IsEnabled(LogLevel.Error))
                {
                    logger.LogError(ex, "Error processing file: {FilePath}", filePath);
                }
            }
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "File system scan complete. New: {New}, Updated: {Updated}, Flagged: {Flagged}",
                result.NewVideos,
                result.UpdatedVideos,
                result.FlaggedForReview);
        }

        return result;
    }

    private async Task ProcessFileAsync(
        string filePath,
        HashSet<string> existingPaths,
        Dictionary<string, VideoIdEntry> existingVideoIds,
        FileSystemScanResult result,
        CancellationToken cancellationToken)
    {
        // Already tracked by path
        if (existingPaths.Contains(filePath))
        {
            return;
        }

        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        string parentFolderName = Path.GetFileName(Path.GetDirectoryName(filePath)) ?? "Unknown";

        // Check if a video with this VideoId already exists but FilePath is missing/wrong
        if (existingVideoIds.TryGetValue(fileNameWithoutExt, out var existingEntry) &&
            existingEntry.FilePath != filePath)
        {
            var existingVideo = await videoRepository.FindOneAsync(new SearchOptions<Video>
            {
                CancellationToken = cancellationToken,
                Query = x => x.Id == existingEntry.Id
            });

            if (existingVideo is not null)
            {
                existingVideo.FilePath = filePath;
                existingVideo.FileSize = new FileInfo(filePath).Length;
                existingVideo.DownloadedAt ??= File.GetLastWriteTimeUtc(filePath);

                // Clear the review flag when the video already has a real title from a platform sync
                // (title differs meaningfully from the bare VideoId or filename stem)
                if (existingVideo.NeedsMetadataReview &&
                    !string.IsNullOrEmpty(existingVideo.Title) &&
                    !string.Equals(existingVideo.Title, existingVideo.VideoId, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(existingVideo.Title, fileNameWithoutExt, StringComparison.OrdinalIgnoreCase))
                {
                    existingVideo.NeedsMetadataReview = false;
                    existingVideo.IsManuallyImported = false;
                }

                await videoRepository.UpdateAsync(existingVideo, ContextOptions.ForCancellationToken(cancellationToken));
                result.UpdatedVideos++;

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Updated file path for video {VideoId}: {FilePath}", fileNameWithoutExt, filePath);
                }
            }

            return;
        }

        // New file - find or create its channel based on the parent folder
        var channel = await FindOrCreateChannelForFolderAsync(parentFolderName, cancellationToken);

        // Try to get metadata from the platform if this channel has a known platform
        Models.Metadata.VideoMetadata? metadata = null;
        if (channel.Platform != "Custom")
        {
            var provider = metadataProviderFactory.GetProviderByPlatform(channel.Platform);
            if (provider is not null)
            {
                try
                {
                    metadata = await provider.GetVideoMetadataAsync(fileNameWithoutExt, cancellationToken);
                }
                catch (Exception ex)
                {
                    if (logger.IsEnabled(LogLevel.Warning))
                    {
                        logger.LogWarning(ex, "Could not fetch metadata for video ID {VideoId} from {Platform}", fileNameWithoutExt, channel.Platform);
                    }
                }
            }
        }

        var fileInfo = new FileInfo(filePath);
        bool needsReview = (metadata is null || metadata.Title == Constants.PrivateVideoTitle) && channel.Platform != "Custom";

        string? thumbnailDataUrl = null;
        if (metadata?.ThumbnailUrl is not null)
        {
            string thumbnailDir = Path.GetDirectoryName(filePath)!;
            thumbnailDataUrl = await thumbnailService.DownloadAndSaveAsync(
                metadata.ThumbnailUrl, thumbnailDir, fileNameWithoutExt, cancellationToken);
        }

        var video = new Video
        {
            VideoId = metadata?.VideoId ?? fileNameWithoutExt,
            Title = metadata?.Title ?? fileNameWithoutExt,
            Description = metadata?.Description,
            Url = metadata?.Url ?? filePath,
            ThumbnailUrl = thumbnailDataUrl ?? metadata?.ThumbnailUrl,
            Platform = channel.Platform,
            Duration = metadata?.Duration,
            UploadDate = metadata?.UploadDate ?? File.GetLastWriteTimeUtc(filePath),
            ViewCount = metadata?.ViewCount,
            LikeCount = metadata?.LikeCount,
            FilePath = filePath,
            FileSize = fileInfo.Length,
            DownloadedAt = fileInfo.LastWriteTimeUtc,
            ChannelId = channel.Id,
            IsManuallyImported = true,
            NeedsMetadataReview = needsReview
        };

        await videoRepository.InsertAsync(video, ContextOptions.ForCancellationToken(cancellationToken));
        result.NewVideos++;

        if (needsReview)
        {
            result.FlaggedForReview++;
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Imported {FilePath} without metadata - flagged for review", filePath);
            }
        }
        else
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Imported {FilePath} with metadata from {Platform}", filePath, channel.Platform);
            }
        }
    }

    private async Task<Channel> FindOrCreateChannelForFolderAsync(string folderName, CancellationToken cancellationToken)
    {
        // Folders are now named after ChannelId (not the display name), so match on that field
        var existing = await channelRepository.FindOneAsync(new SearchOptions<Channel>
        {
            CancellationToken = cancellationToken,
            Query = x => x.ChannelId == folderName
        });

        if (existing is not null)
        {
            return existing;
        }

        // Create a placeholder channel for unrecognised folders.
        // The folder name IS the ChannelId; admins can update the display name and platform later.
        var channel = new Channel
        {
            ChannelId = folderName,
            Name = folderName,
            Url = $"custom://{folderName}",
            Platform = "YouTube",
            SubscribedAt = DateTime.UtcNow
        };

        await channelRepository.InsertAsync(channel, ContextOptions.ForCancellationToken(cancellationToken));

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Created placeholder channel for folder '{FolderName}'", folderName);
        }

        return channel;
    }

    private static IEnumerable<string> EnumerateVideoFiles(string rootPath) =>
        Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories)
            .Where(f => VideoExtensions.Contains(
                Path.GetExtension(f),
                StringComparer.OrdinalIgnoreCase));
}

public class FileSystemScanResult
{
    public int NewVideos { get; set; }
    public int UpdatedVideos { get; set; }
    public int FlaggedForReview { get; set; }
}