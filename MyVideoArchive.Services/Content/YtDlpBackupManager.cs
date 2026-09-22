using System.Text.Json;

namespace MyVideoArchive.Services.Content;

/// <summary>
/// Manages the on-disk backup of the previous yt-dlp install so an admin can trigger an update
/// on demand (see <see cref="Jobs.YtDlpUpdateJob"/>) and roll back to the last known-good version
/// if the new release misbehaves, without waiting for a redeploy.
/// </summary>
/// <remarks>
/// For the standalone-binary update methods ("self" / "binary") yt-dlp is a single self-contained
/// executable, so backup/rollback is a straight file copy: the current executable is copied into
/// the backup folder before the update runs, and rollback copies it back over the live path.
///
/// For the "pip" method (used by the Docker image — see the Dockerfile) yt-dlp is installed as a
/// Python package. The file at the configured executable path is just a tiny launcher script, not
/// the implementation itself, so copying it would not actually roll anything back. Instead the
/// currently-installed version string is recorded, and rollback re-installs that exact pinned
/// version via pip.
///
/// The backup folder defaults to a subfolder of the persisted downloads volume
/// (<c>VideoDownload:OutputPath</c>) — overridable via <c>YoutubeDL:BackupPath</c> — rather than
/// living next to the yt-dlp executable, so it survives container recreation even in the Docker
/// deployment, where the executable itself does not.
/// </remarks>
public class YtDlpBackupManager
{
    private const string MetadataFileName = "backup-info.json";

    private readonly IConfiguration configuration;

    public YtDlpBackupManager(IConfiguration configuration)
    {
        this.configuration = configuration;
    }

    /// <summary>
    /// Resolves the backup folder, honouring an explicit <c>YoutubeDL:BackupPath</c> override.
    /// </summary>
    public string ResolveBackupDirectory()
    {
        string? configured = configuration["YoutubeDL:BackupPath"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        string outputPath = configuration["VideoDownload:OutputPath"]
            ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

        return Path.Combine(outputPath, "_System", "yt-dlp-backup");
    }

    /// <summary>
    /// Reads the metadata for the currently available backup, or null if none has been taken yet
    /// (or it could not be parsed).
    /// </summary>
    public async Task<YtDlpBackupMetadata?> TryReadMetadataAsync(CancellationToken cancellationToken = default)
    {
        string metadataPath = Path.Combine(ResolveBackupDirectory(), MetadataFileName);
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(metadataPath);
            return await JsonSerializer.DeserializeAsync<YtDlpBackupMetadata>(stream, cancellationToken: cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Clears the backup folder and stores a backup of the currently-installed yt-dlp (its
    /// executable, for file-based methods, plus metadata always). Called immediately before every
    /// update — scheduled or manual — so a rollback is always available afterwards.
    /// </summary>
    public async Task BackupCurrentAsync(
        string ytDlpPath,
        string method,
        string? currentVersion,
        string? pipPackage,
        CancellationToken cancellationToken = default)
    {
        string backupDir = ResolveBackupDirectory();

        if (Directory.Exists(backupDir))
        {
            Directory.Delete(backupDir, recursive: true);
        }

        Directory.CreateDirectory(backupDir);

        if (IsFileBasedMethod(method) && File.Exists(ytDlpPath))
        {
            string destination = Path.Combine(backupDir, Path.GetFileName(ytDlpPath));
            File.Copy(ytDlpPath, destination, overwrite: true);
        }

        var metadata = new YtDlpBackupMetadata(method, currentVersion, DateTime.UtcNow, pipPackage);
        string metadataPath = Path.Combine(backupDir, MetadataFileName);
        await using var stream = File.Create(metadataPath);
        await JsonSerializer.SerializeAsync(stream, metadata, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
    }

    /// <summary>
    /// Copies the backed-up executable back over <paramref name="ytDlpPath"/>. Only meaningful
    /// for file-based backups (method "self"/"binary"); returns false if there is no backup file
    /// to restore (e.g. the backup was taken for a pip install).
    /// </summary>
    public bool RestoreBackupFile(string ytDlpPath)
    {
        string backupFile = Path.Combine(ResolveBackupDirectory(), Path.GetFileName(ytDlpPath));
        if (!File.Exists(backupFile))
        {
            return false;
        }

        File.Copy(backupFile, ytDlpPath, overwrite: true);
        return true;
    }

    public static bool IsFileBasedMethod(string method) => method is "self" or "binary";
}
