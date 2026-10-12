using System.Diagnostics;
using System.Text;

namespace MyVideoArchive.Services.Content;

/// <summary>
/// Small process-execution helpers shared by <see cref="Jobs.YtDlpUpdateJob"/> (scheduled/manual
/// update + rollback) and <see cref="YtDlpMaintenanceService"/> (status reporting) so both agree
/// on exactly how yt-dlp's version is read and external processes are invoked/logged.
/// </summary>
public static class YtDlpProcessHelper
{
    /// <summary>
    /// Runs <c>&lt;ytDlpPath&gt; --version</c> and returns the trimmed output, or null if the
    /// executable is missing, not runnable, or the check otherwise fails.
    /// </summary>
    public static async Task<string?> TryGetVersionAsync(string ytDlpPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await RunProcessAsync(ytDlpPath, "--version", cancellationToken);
            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                return result.StandardOutput.Trim();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Non-fatal — callers treat a null version as "unknown".
        }

        return null;
    }

    public static async Task<ProcessResult> RunProcessAsync(
        string fileName, string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                standardOutput.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                standardError.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(
            process.ExitCode,
            standardOutput.ToString().Trim(),
            standardError.ToString().Trim());
    }

    public static void LogProcessOutput(ILogger logger, string command, ProcessResult result)
    {
        if (result.ExitCode == 0)
        {
            if (logger.IsEnabled(LogLevel.Debug) && !string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                logger.LogDebug("'{Command}' output: {Output}", command, result.StandardOutput);
            }
        }
        else if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning(
                "'{Command}' exited with code {ExitCode}. {Error}",
                command, result.ExitCode,
                string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError);
        }
    }

    public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
