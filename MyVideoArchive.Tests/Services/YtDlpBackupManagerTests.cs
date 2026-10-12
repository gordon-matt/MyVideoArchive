namespace MyVideoArchive.Tests.Services;

public class YtDlpBackupManagerTests
{
    private static YtDlpBackupManager CreateManager(string outputPath) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["VideoDownload:OutputPath"] = outputPath })
            .Build());

    [Fact]
    public async Task TryReadMetadataAsync_WhenNoBackupTaken_ReturnsNull()
    {
        string root = CreateTempDir();
        try
        {
            var manager = CreateManager(root);
            Assert.Null(await manager.TryReadMetadataAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BackupCurrentAsync_FileBasedMethod_CopiesExecutableAndWritesMetadata()
    {
        string root = CreateTempDir();
        try
        {
            string ytDlpPath = Path.Combine(root, "yt-dlp.exe");
            await File.WriteAllTextAsync(ytDlpPath, "fake-binary-v1");

            var manager = CreateManager(root);
            await manager.BackupCurrentAsync(ytDlpPath, "self", "2026.01.01", pipPackage: null);

            var metadata = await manager.TryReadMetadataAsync();
            Assert.NotNull(metadata);
            Assert.Equal("self", metadata!.Method);
            Assert.Equal("2026.01.01", metadata.Version);

            string backupFile = Path.Combine(manager.ResolveBackupDirectory(), "yt-dlp.exe");
            Assert.True(File.Exists(backupFile));
            Assert.Equal("fake-binary-v1", await File.ReadAllTextAsync(backupFile));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BackupCurrentAsync_PipMethod_DoesNotCopyFileButRecordsVersion()
    {
        string root = CreateTempDir();
        try
        {
            string ytDlpPath = Path.Combine(root, "yt-dlp");
            await File.WriteAllTextAsync(ytDlpPath, "#!/usr/bin/env python3");

            var manager = CreateManager(root);
            await manager.BackupCurrentAsync(ytDlpPath, "pip", "2026.01.01", "yt-dlp[default]");

            string backupFile = Path.Combine(manager.ResolveBackupDirectory(), "yt-dlp");
            Assert.False(File.Exists(backupFile));

            var metadata = await manager.TryReadMetadataAsync();
            Assert.NotNull(metadata);
            Assert.Equal("pip", metadata!.Method);
            Assert.Equal("2026.01.01", metadata.Version);
            Assert.Equal("yt-dlp[default]", metadata.PipPackage);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BackupCurrentAsync_ClearsPreviousBackupBeforeWritingNewOne()
    {
        string root = CreateTempDir();
        try
        {
            string ytDlpPath = Path.Combine(root, "yt-dlp.exe");
            var manager = CreateManager(root);

            await File.WriteAllTextAsync(ytDlpPath, "v1");
            await manager.BackupCurrentAsync(ytDlpPath, "self", "1.0", null);

            await File.WriteAllTextAsync(ytDlpPath, "v2");
            await manager.BackupCurrentAsync(ytDlpPath, "self", "2.0", null);

            var metadata = await manager.TryReadMetadataAsync();
            Assert.Equal("2.0", metadata!.Version);

            string backupFile = Path.Combine(manager.ResolveBackupDirectory(), "yt-dlp.exe");
            Assert.Equal("v2", await File.ReadAllTextAsync(backupFile));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreBackupFile_WhenBackupExists_OverwritesLivePathAndReturnsTrue()
    {
        string root = CreateTempDir();
        try
        {
            string ytDlpPath = Path.Combine(root, "yt-dlp.exe");
            var manager = CreateManager(root);

            await File.WriteAllTextAsync(ytDlpPath, "old-version");
            await manager.BackupCurrentAsync(ytDlpPath, "self", "1.0", null);

            await File.WriteAllTextAsync(ytDlpPath, "new-version-that-broke-things");

            bool restored = manager.RestoreBackupFile(ytDlpPath);

            Assert.True(restored);
            Assert.Equal("old-version", await File.ReadAllTextAsync(ytDlpPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RestoreBackupFile_WhenNoBackupExists_ReturnsFalse()
    {
        string root = CreateTempDir();
        try
        {
            string ytDlpPath = Path.Combine(root, "yt-dlp.exe");
            var manager = CreateManager(root);

            Assert.False(manager.RestoreBackupFile(ytDlpPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("self", true)]
    [InlineData("binary", true)]
    [InlineData("pip", false)]
    public void IsFileBasedMethod_ReturnsExpected(string method, bool expected)
    {
        Assert.Equal(expected, YtDlpBackupManager.IsFileBasedMethod(method));
    }

    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mva-ytdlp-backup-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }
}
