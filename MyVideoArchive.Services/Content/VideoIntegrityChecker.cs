using System.Diagnostics;
using System.Text;

namespace MyVideoArchive.Services.Content;

/// <summary>
/// Verifies that a downloaded video file's video stream can be demuxed without errors.
/// </summary>
/// <remarks>
/// Runs a fast ffmpeg stream-copy pass (<c>-c copy</c>, no pixel decoding) that reads through
/// every NAL unit in the video stream. This catches container/bitstream corruption — e.g.
/// malformed NAL unit lengths introduced by a buggy remux step — that ffprobe's metadata-only
/// inspection does not detect. With <c>-xerror</c>, ffmpeg aborts at the first fatal error, so a
/// corrupt file fails almost instantly rather than after a slow full decode.
/// </remarks>
public static class VideoIntegrityChecker
{
    public static async Task<VideoIntegrityResult> VerifyAsync(
        string? ffmpegPath,
        string videoFilePath,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
        {
            if (logger?.IsEnabled(LogLevel.Warning) == true)
            {
                logger.LogWarning(
                    "ffmpeg not found at '{Path}'; skipping post-download integrity check for {File}.",
                    ffmpegPath, videoFilePath);
            }

            return VideoIntegrityResult.Skipped;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            ArgumentList =
            {
                "-v", "error",
                "-xerror",
                "-nostdin",
                "-i", videoFilePath,
                "-map", "0:v:0",
                "-c", "copy",
                "-f", "null",
                "-",
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using var process = new Process { StartInfo = startInfo };
            var stderr = new StringBuilder();

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null && stderr.Length < 4000)
                {
                    stderr.AppendLine(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0)
            {
                return VideoIntegrityResult.Valid;
            }

            string error = stderr.ToString().Trim();
            if (logger?.IsEnabled(LogLevel.Warning) == true)
            {
                logger.LogWarning(
                    "Integrity check failed for {File} (ffmpeg exit code {ExitCode}): {Error}",
                    videoFilePath, process.ExitCode, error);
            }

            return VideoIntegrityResult.Invalid(string.IsNullOrEmpty(error)
                ? $"ffmpeg exited with code {process.ExitCode}"
                : error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (logger?.IsEnabled(LogLevel.Warning) == true)
            {
                logger.LogWarning(ex, "Could not run integrity check for {File}; skipping.", videoFilePath);
            }

            return VideoIntegrityResult.Skipped;
        }
    }
}

/// <param name="IsValid">True when the file passed the check, or the check was skipped.</param>
/// <param name="WasSkipped">True when the check could not run (e.g. ffmpeg missing) and was treated as a pass.</param>
/// <param name="Error">Failure detail from ffmpeg's stderr, when <see cref="IsValid"/> is false.</param>
public readonly record struct VideoIntegrityResult(bool IsValid, bool WasSkipped, string? Error)
{
    public static readonly VideoIntegrityResult Valid = new(true, false, null);
    public static readonly VideoIntegrityResult Skipped = new(true, true, null);
    public static VideoIntegrityResult Invalid(string error) => new(false, false, error);
}
