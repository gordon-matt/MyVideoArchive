using System.Diagnostics;
using System.Text;
using Hangfire;
using MyVideoArchive.Infrastructure;
using YoutubeDLSharp;

namespace MyVideoArchive.Services.Jobs;

/// <summary>
/// Hangfire job that keeps the yt-dlp binary up to date.
///
/// YouTube regularly changes its internal APIs, and an out-of-date yt-dlp starts failing
/// channel/playlist/video fetches with errors like "HTTP Error 400: Bad Request" /
/// "Request contains an invalid argument". yt-dlp itself prints a warning once its build is
/// older than 90 days. This job checks for and installs the latest release so archiving keeps
/// working without manual intervention. It can also be triggered on demand from the Hangfire
/// dashboard (/hangfire).
///
/// Runs in the dedicated "downloads" queue so it serialises with <see cref="VideoDownloadJob"/>
/// and <see cref="SubtitleBackfillJob"/> — this avoids swapping the yt-dlp binary out from under
/// an in-flight download (on Windows, replacing a running executable fails outright).
/// </summary>
[Queue("downloads")]
public class YtDlpUpdateJob
{
    private readonly ILogger<YtDlpUpdateJob> logger;
    private readonly IConfiguration configuration;
    private readonly YoutubeDL ytdl;

    public YtDlpUpdateJob(
        ILogger<YtDlpUpdateJob> logger,
        IConfiguration configuration,
        YoutubeDL ytdl)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.ytdl = ytdl;
    }

    [HangfireSkipWhenPreviousInstanceIsRunningFilter]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        // Checked at execution time so toggling the flag in appsettings takes effect on the
        // next run without a redeploy.
        if (!configuration.GetValue<bool>("YoutubeDL:AutoUpdate:Enabled", true))
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("yt-dlp auto-update skipped — YoutubeDL:AutoUpdate:Enabled is false");
            }
            return;
        }

        string ytDlpPath = ytdl.YoutubeDLPath;
        if (string.IsNullOrWhiteSpace(ytDlpPath))
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("yt-dlp auto-update skipped — yt-dlp path is not configured");
            }
            return;
        }

        string method = ResolveUpdateMethod();

        string? versionBefore = await TryGetVersionAsync(ytDlpPath, cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Starting yt-dlp auto-update (method: {Method}, current version: {Version})",
                method, versionBefore ?? "unknown");
        }

        bool ran;
        try
        {
            ran = method switch
            {
                "pip" => await UpdateViaPipAsync(cancellationToken),
                "binary" => await UpdateViaBinaryDownloadAsync(ytDlpPath, cancellationToken),
                _ => await UpdateViaSelfAsync(ytDlpPath, cancellationToken),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "yt-dlp auto-update failed (method: {Method})", method);
            }
            return;
        }

        if (!ran)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("yt-dlp auto-update did not complete successfully (method: {Method})", method);
            }
            return;
        }

        string? versionAfter = await TryGetVersionAsync(ytDlpPath, cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            if (!string.IsNullOrEmpty(versionAfter) &&
                string.Equals(versionBefore, versionAfter, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("yt-dlp is already up to date (version: {Version})", versionAfter);
            }
            else
            {
                logger.LogInformation(
                    "yt-dlp updated from {Before} to {After}",
                    versionBefore ?? "unknown", versionAfter ?? "unknown");
            }
        }
    }

    /// <summary>
    /// Resolves how yt-dlp should be updated. When explicitly configured
    /// (<c>YoutubeDL:AutoUpdate:Method</c> = self | pip | binary) that wins. Otherwise the
    /// method is inferred: a configured <c>YoutubeDL:ExecutablePath</c> means the binary is
    /// provided by the image (Docker installs yt-dlp via pip), so we update through pip.
    /// A missing path means the standalone binary YoutubeDLSharp downloaded on first run,
    /// which supports in-place self-update via <c>-U</c>.
    /// </summary>
    private string ResolveUpdateMethod()
    {
        string? configured = configuration["YoutubeDL:AutoUpdate:Method"];
        if (!string.IsNullOrWhiteSpace(configured) &&
            !configured.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            return configured.Trim().ToLowerInvariant();
        }

        string? execPath = configuration["YoutubeDL:ExecutablePath"];
        return string.IsNullOrWhiteSpace(execPath) ? "self" : "pip";
    }

    /// <summary>
    /// Runs yt-dlp's built-in self-updater (<c>yt-dlp -U</c>). Works for the standalone
    /// binaries used in local/desktop installs.
    /// </summary>
    private async Task<bool> UpdateViaSelfAsync(string ytDlpPath, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(ytDlpPath, "-U", cancellationToken);
        LogProcessOutput("yt-dlp -U", result);
        return result.ExitCode == 0;
    }

    /// <summary>
    /// Re-downloads the latest standalone yt-dlp binary next to the current one via
    /// YoutubeDLSharp's downloader.
    /// </summary>
    private async Task<bool> UpdateViaBinaryDownloadAsync(string ytDlpPath, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(ytDlpPath);
        await Utils.DownloadYtDlp(directory ?? string.Empty).WaitAsync(cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Downloaded latest yt-dlp binary to {Path}", ytDlpPath);
        }

        return File.Exists(ytDlpPath);
    }

    /// <summary>
    /// Upgrades yt-dlp through pip. Used for container images where yt-dlp is installed as a
    /// Python package (see Dockerfile). Note: in an ephemeral container the upgrade lives only
    /// until the container is recreated, at which point the image's pinned version is restored —
    /// the recurring job simply re-applies the upgrade on its next run.
    /// </summary>
    private async Task<bool> UpdateViaPipAsync(CancellationToken cancellationToken)
    {
        string pipExecutable = configuration["YoutubeDL:AutoUpdate:PipExecutable"] ?? "pip3";
        string pipArguments = configuration["YoutubeDL:AutoUpdate:PipArguments"]
            ?? "install --break-system-packages --no-cache-dir --upgrade";
        string pipPackage = configuration["YoutubeDL:AutoUpdate:PipPackage"] ?? "yt-dlp[default]";

        var result = await RunProcessAsync(pipExecutable, $"{pipArguments} {pipPackage}", cancellationToken);
        LogProcessOutput($"{pipExecutable} {pipArguments} {pipPackage}", result);
        return result.ExitCode == 0;
    }

    private async Task<string?> TryGetVersionAsync(string ytDlpPath, CancellationToken cancellationToken)
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
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(ex, "Unable to read yt-dlp version");
            }
        }

        return null;
    }

    private void LogProcessOutput(string command, ProcessResult result)
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

    private static async Task<ProcessResult> RunProcessAsync(
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

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
