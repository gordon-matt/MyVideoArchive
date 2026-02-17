using Extenso.Data.Entity;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MyVideoArchive.Data.Entities;
using MyVideoArchive.Infrastructure;

namespace MyVideoArchive.Services.Jobs;

/// <summary>
/// Hangfire job for downloading videos
/// </summary>
public class VideoDownloadJob
{
    private readonly ILogger<VideoDownloadJob> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly VideoDownloaderFactory _downloaderFactory;
    private readonly IConfiguration _configuration;

    public VideoDownloadJob(
        ILogger<VideoDownloadJob> logger,
        IServiceProvider serviceProvider,
        VideoDownloaderFactory downloaderFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _downloaderFactory = downloaderFactory;
        _configuration = configuration;
    }

    [HangfireSkipWhenPreviousInstanceIsRunningFilter]
    //[DisableConcurrentExecution(timeoutInSeconds: 7200)] // 2 hours timeout
    public async Task ExecuteAsync(int videoId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting video download job for video ID: {VideoId}", videoId);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var videoRepository = scope.ServiceProvider.GetRequiredService<IRepository<Video>>();

            var video = await videoRepository.FindOneAsync(new SearchOptions<Video>
            {
                CancellationToken = cancellationToken,
                Query = x => x.Id == videoId,
                Include = query => query.Include(x => x.Channel)
            });

            if (video == null)
            {
                _logger.LogWarning("Video with ID {VideoId} not found", videoId);
                return;
            }

            // Skip if already downloaded
            if (!string.IsNullOrEmpty(video.FilePath) && File.Exists(video.FilePath))
            {
                _logger.LogInformation("Video {VideoId} already downloaded", videoId);
                return;
            }

            // Get the appropriate downloader
            var downloader = _downloaderFactory.GetDownloaderByPlatform(video.Platform);
            if (downloader == null)
            {
                _logger.LogError("No downloader found for platform: {Platform}", video.Platform);
                return;
            }

            // Get download path from configuration
            var downloadPath = _configuration.GetValue<string>("VideoDownload:OutputPath") 
                ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

            // Create channel-specific subdirectory
            var channelPath = Path.Combine(downloadPath, SanitizeFileName(video.Channel.Name));

            // Download the video
            var progress = new Progress<double>(p =>
            {
                _logger.LogInformation("Download progress for video {VideoId}: {Progress:P0}", videoId, p);
            });

            var filePath = await downloader.DownloadVideoAsync(
                video.Url,
                channelPath,
                progress,
                cancellationToken);

            // Update video record
            video.FilePath = filePath;
            video.FileSize = new FileInfo(filePath).Length;
            video.DownloadedAt = DateTime.UtcNow;

            await videoRepository.UpdateAsync(video);

            _logger.LogInformation("Successfully downloaded video {VideoId} to {FilePath}", videoId, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading video {VideoId}", videoId);
            throw;
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
    }
}
