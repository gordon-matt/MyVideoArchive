using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YoutubeDLSharp;

namespace MyVideoArchive.Services;

public class VideoDownloadService : BackgroundService
{
    private readonly ILogger<VideoDownloadService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private YoutubeDL? _ytdl;

    public VideoDownloadService(
        ILogger<VideoDownloadService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Video Download Service is starting.");

        // Initialize yt-dlp
        await InitializeYoutubeDL();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Video Download Service is running at: {time}", DateTimeOffset.Now);

                // Here you would:
                // 1. Check for channels that need to be checked for new videos
                // 2. Check for videos that need to be downloaded
                // 3. Process the download queue

                // For now, we'll just log
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in Video Download Service.");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        _logger.LogInformation("Video Download Service is stopping.");
    }

    private async Task InitializeYoutubeDL()
    {
        try
        {
            // Download yt-dlp and ffmpeg if not already present
            await YoutubeDLSharp.Utils.DownloadYtDlp();
            await YoutubeDLSharp.Utils.DownloadFFmpeg();

            _ytdl = new YoutubeDL
            {
                YoutubeDLPath = Path.Combine(Directory.GetCurrentDirectory(), "yt-dlp.exe"),
                FFmpegPath = Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg.exe"),
                OutputFolder = Path.Combine(Directory.GetCurrentDirectory(), "Downloads")
            };

            // Create downloads folder if it doesn't exist
            if (!Directory.Exists(_ytdl.OutputFolder))
            {
                Directory.CreateDirectory(_ytdl.OutputFolder);
            }

            _logger.LogInformation("YoutubeDL initialized successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize YoutubeDL.");
        }
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Video Download Service is stopping.");
        await base.StopAsync(stoppingToken);
    }
}
