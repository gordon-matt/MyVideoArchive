using Microsoft.Extensions.Logging.Abstractions;

namespace MyVideoArchive.Services.Content;

/// <summary>
/// Verifies that a downloaded video file's video stream can be demuxed without errors.
/// The check itself lives in <see cref="IVideoIntegrityService"/>.
/// </summary>
public static class VideoIntegrityChecker
{
    public static Task<VideoIntegrityResult> VerifyAsync(
        string? ffmpegPath,
        string videoFilePath,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        ILogger<VideoIntegrityService> typed = logger is null
            ? NullLogger<VideoIntegrityService>.Instance
            : new LoggerAdapter<VideoIntegrityService>(logger);

        return new VideoIntegrityService(typed).VerifyAsync(ffmpegPath, videoFilePath, cancellationToken);
    }
}