using YoutubeDLSharp;

namespace MyVideoArchive.Services;

/// <summary>
/// Service for initializing and configuring YoutubeDL instances
/// </summary>
public class YoutubeDLInitializer
{
    private readonly ILogger<YoutubeDLInitializer> _logger;
    private readonly IConfiguration _configuration;
    private static YoutubeDL? _instance;
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    public YoutubeDLInitializer(
        ILogger<YoutubeDLInitializer> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<YoutubeDL> GetInstanceAsync()
    {
        if (_instance != null)
        {
            return _instance;
        }

        await _initLock.WaitAsync();
        try
        {
            if (_instance != null)
            {
                return _instance;
            }

            _logger.LogInformation("Initializing YoutubeDL...");

            // Get paths from configuration or use defaults
            var ytDlpPath = _configuration.GetValue<string>("YoutubeDL:ExecutablePath") 
                ?? Path.Combine(Directory.GetCurrentDirectory(), "yt-dlp.exe");
            
            var ffmpegPath = _configuration.GetValue<string>("YoutubeDL:FFmpegPath") 
                ?? Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg.exe");

            var downloadPath = _configuration.GetValue<string>("VideoDownload:OutputPath") 
                ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

            // Download yt-dlp and ffmpeg if not already present
            if (!File.Exists(ytDlpPath))
            {
                _logger.LogInformation("Downloading yt-dlp...");
                await YoutubeDLSharp.Utils.DownloadYtDlp();
            }

            if (!File.Exists(ffmpegPath))
            {
                _logger.LogInformation("Downloading ffmpeg...");
                await YoutubeDLSharp.Utils.DownloadFFmpeg();
            }

            // Create downloads folder if it doesn't exist
            if (!Directory.Exists(downloadPath))
            {
                Directory.CreateDirectory(downloadPath);
                _logger.LogInformation("Created downloads directory: {Path}", downloadPath);
            }

            _instance = new YoutubeDL
            {
                YoutubeDLPath = ytDlpPath,
                FFmpegPath = ffmpegPath,
                OutputFolder = downloadPath
            };

            _logger.LogInformation("YoutubeDL initialized successfully");
            return _instance;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize YoutubeDL");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
