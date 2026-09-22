using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace MyVideoArchive.Services.Content;

/// <summary>
/// Optional post-download safety net that shrinks videos taller than a configured maximum
/// height using ffmpeg.
/// </summary>
/// <remarks>
/// Some platforms (notably Odysee/LBRY) expose only a single "original" quality through
/// yt-dlp — there is no lower-resolution format to select, so <c>VideoDownload:VideoQuality</c>
/// has nothing to act on. This is the only way to actually reduce those files' size. Controlled
/// by the <c>VideoDownload:PostProcessDownscale</c> config section (disabled by default). Videos
/// already at or below the configured height are left completely untouched (only probed).
/// </remarks>
public static class VideoDownscaleService
{
    public static async Task<string> EnsureMaxHeightAsync(
        string? ffmpegPath,
        string videoFilePath,
        IConfiguration configuration,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        bool enabled = configuration.GetValue("VideoDownload:PostProcessDownscale:Enabled", false);
        if (!enabled || string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath) || !File.Exists(videoFilePath))
        {
            return videoFilePath;
        }

        int maxHeight = configuration.GetValue("VideoDownload:PostProcessDownscale:MaxHeight", 720);
        int crf = configuration.GetValue("VideoDownload:PostProcessDownscale:Crf", 23);
        string preset = configuration.GetValue<string>("VideoDownload:PostProcessDownscale:Preset") ?? "medium";

        try
        {
            string ffprobePath = ResolveFfprobePath(ffmpegPath);
            int? height = await ProbeHeightAsync(ffprobePath, videoFilePath, cancellationToken);
            if (height is null || height <= maxHeight)
            {
                return videoFilePath;
            }

            if (logger?.IsEnabled(LogLevel.Information) == true)
            {
                logger.LogInformation(
                    "Downscaling {File} from {Height}p to {MaxHeight}p (yt-dlp had no smaller format to select).",
                    videoFilePath, height, maxHeight);
            }

            string tempOutput = Path.Combine(
                Path.GetTempPath(), $"{Guid.NewGuid():N}{Path.GetExtension(videoFilePath)}");

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                ArgumentList =
                {
                    "-y",
                    "-nostdin",
                    "-i", videoFilePath,
                    "-map", "0:v:0",
                    "-map", "0:a:0?",
                    "-vf", $"scale=-2:{maxHeight}",
                    "-c:v", "libx264",
                    "-preset", preset,
                    "-crf", crf.ToString(CultureInfo.InvariantCulture),
                    "-c:a", "copy",
                    "-movflags", "+faststart",
                    "-f", "mp4",
                    tempOutput,
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            (int exitCode, string error) = await RunAsync(startInfo, cancellationToken);

            if (exitCode != 0 || !File.Exists(tempOutput) || new FileInfo(tempOutput).Length == 0)
            {
                if (logger?.IsEnabled(LogLevel.Warning) == true)
                {
                    logger.LogWarning(
                        "Downscale of {File} failed (ffmpeg exit code {ExitCode}); keeping original. {Error}",
                        videoFilePath, exitCode, error);
                }

                TryDelete(tempOutput);
                return videoFilePath;
            }

            File.Delete(videoFilePath);
            File.Move(tempOutput, videoFilePath);

            return videoFilePath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (logger?.IsEnabled(LogLevel.Warning) == true)
            {
                logger.LogWarning(ex, "Error downscaling {File}; keeping original.", videoFilePath);
            }

            return videoFilePath;
        }
    }

    private static string ResolveFfprobePath(string ffmpegPath)
    {
        string dir = Path.GetDirectoryName(ffmpegPath) ?? string.Empty;
        string ffmpegFileName = Path.GetFileName(ffmpegPath);
        string ffprobeFileName = ffmpegFileName.Replace("ffmpeg", "ffprobe", StringComparison.OrdinalIgnoreCase);
        return Path.Combine(dir, ffprobeFileName);
    }

    private static async Task<int?> ProbeHeightAsync(
        string ffprobePath, string videoFilePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(ffprobePath))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            ArgumentList =
            {
                "-v", "error",
                "-select_streams", "v:0",
                "-show_entries", "stream=height",
                "-of", "csv=p=0",
                videoFilePath,
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        (int exitCode, string output) = await RunAsync(startInfo, cancellationToken, captureStdOut: true);
        if (exitCode != 0)
        {
            return null;
        }

        string firstLine = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

        return int.TryParse(firstLine, NumberStyles.Integer, CultureInfo.InvariantCulture, out int height)
            ? height
            : null;
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        ProcessStartInfo startInfo, CancellationToken cancellationToken, bool captureStdOut = false)
    {
        using var process = new Process { StartInfo = startInfo };
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null && standardOutput.Length < 4000)
            {
                standardOutput.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null && standardError.Length < 4000)
            {
                standardError.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, (captureStdOut ? standardOutput : standardError).ToString().Trim());
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
