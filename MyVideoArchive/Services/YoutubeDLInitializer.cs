using YoutubeDLSharp;

namespace MyVideoArchive.Services;

/// <summary>
/// Service for initializing and configuring YoutubeDL instances
/// </summary>
public class YoutubeDLInitializer
{
    private readonly ILogger<YoutubeDLInitializer> logger;
    private readonly IConfiguration configuration;
    private static YoutubeDL? ytdl;
    private static readonly SemaphoreSlim initLock = new(1, 1);

    public YoutubeDLInitializer(
        ILogger<YoutubeDLInitializer> logger,
        IConfiguration configuration)
    {
        this.logger = logger;
        this.configuration = configuration;
    }

    public async Task<YoutubeDL> GetInstanceAsync()
    {
        if (ytdl != null)
        {
            return ytdl;
        }

        await initLock.WaitAsync();

        try
        {
            if (ytdl != null)
            {
                return ytdl;
            }

            logger.LogInformation("Initializing YoutubeDL...");

            // Get paths from configuration or use defaults
            string ytDlpPath = configuration.GetValue<string>("YoutubeDL:ExecutablePath")
                ?? Path.Combine(Directory.GetCurrentDirectory(), "yt-dlp.exe");

            string ffmpegPath = configuration.GetValue<string>("YoutubeDL:FFmpegPath")
                ?? Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg.exe");

            string downloadPath = configuration.GetValue<string>("VideoDownload:OutputPath")
                ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

            // Download yt-dlp and ffmpeg if not already present
            if (!File.Exists(ytDlpPath))
            {
                logger.LogInformation("Downloading yt-dlp...");
                await Utils.DownloadYtDlp();
            }

            if (!File.Exists(ffmpegPath))
            {
                logger.LogInformation("Downloading ffmpeg...");
                await Utils.DownloadFFmpeg();
            }

            // Create downloads folder if it doesn't exist
            if (!Directory.Exists(downloadPath))
            {
                Directory.CreateDirectory(downloadPath);
                logger.LogInformation("Created downloads directory: {Path}", downloadPath);
            }

            ytdl = new YoutubeDL
            {
                YoutubeDLPath = ytDlpPath,
                FFmpegPath = ffmpegPath,
                OutputFolder = downloadPath
            };

            logger.LogInformation("YoutubeDL initialized successfully");
            return ytdl;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize YoutubeDL");
            throw;
        }
        finally
        {
            initLock.Release();
        }
    }
}