using MyVideoArchive.Tests.TestHelpers;

namespace MyVideoArchive.Tests.Services.Jobs;

public class FileSystemScanJobTests
{
    private static FileSystemScanJob CreateScanJob(
        InMemoryDatabaseFixture db,
        IConfiguration configuration,
        IAdditionalContentService? additionalContentService = null) =>
        new(
            NullLogger<FileSystemScanJob>.Instance,
            configuration,
            db.AdditionalContentRepository,
            db.ChannelRepository,
            db.CustomPlaylistVideoRepository,
            db.PlaylistRepository,
            db.PlaylistVideoRepository,
            db.SeriesRepository,
            db.SeriesPlaylistRepository,
            db.UserPlaylistVideoRepository,
            db.UserVideoRepository,
            db.VideoRepository,
            db.VideoTagRepository,
            additionalContentService ?? Mock.Of<IAdditionalContentService>(),
            new ThumbnailService(NullLogger<ThumbnailService>.Instance, Mock.Of<IHttpClientFactory>()));

    [Fact]
    public void Source_DoesNotUseFilesystemDeletionOrAdditionalContentDeleteAsync()
    {
        string sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "MyVideoArchive.Services", "Jobs", "FileSystemScanJob.cs"));

        Assert.True(File.Exists(sourcePath), $"Expected scanner source at {sourcePath}");
        string source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("File.Delete", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.Delete", source, StringComparison.Ordinal);
        Assert.DoesNotContain("additionalContentService.DeleteAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_CustomChannelScan_DoesNotDeleteLibraryFilesOnDisk()
    {
        string root = Path.Combine(Path.GetTempPath(), "mva-scan-safe-" + Guid.NewGuid().ToString("N"));
        string channelFolderName = "ScanSafetyChannel";
        string playlistDir = Path.Combine(root, "_Custom", channelFolderName, "Course1");
        string extrasDir = Path.Combine(playlistDir, "_extras", "01 - Lesson");
        Directory.CreateDirectory(extrasDir);

        string videoPath = Path.Combine(playlistDir, "01 - Lesson.mp4");
        string srtPath = Path.Combine(playlistDir, "01 - Lesson.srt");
        string extraPath = Path.Combine(extrasDir, "notes.txt");
        await File.WriteAllBytesAsync(videoPath, [0x00, 0x00, 0x00]);
        await File.WriteAllTextAsync(srtPath, "1\n00:00:00,000 --> 00:00:01,000\nHi\n");
        await File.WriteAllTextAsync(extraPath, "extra notes");

        try
        {
            using var db = new InMemoryDatabaseFixture();
            var channel = await db.ChannelRepository.InsertAsync(new Channel
            {
                ChannelId = channelFolderName,
                Name = channelFolderName,
                Url = "custom://local",
                Platform = "Custom",
                SubscribedAt = DateTime.UtcNow
            });

            await db.AdditionalContentRepository.InsertAsync(new AdditionalContentItem
            {
                FileName = "notes.txt",
                FilePath = extraPath,
                ChannelId = channel.Id,
                FileSize = 11,
                UploadedAt = DateTime.UtcNow
            });
            await db.AdditionalContentRepository.InsertAsync(new AdditionalContentItem
            {
                FileName = "ghost.txt",
                FilePath = Path.Combine(playlistDir, "missing-on-disk.txt"),
                ChannelId = channel.Id,
                FileSize = 1,
                UploadedAt = DateTime.UtcNow
            });
            await db.AdditionalContentRepository.InsertAsync(new AdditionalContentItem
            {
                FileName = "@SynoEAStream",
                FilePath = Path.Combine(playlistDir, "@eaDir", "@SynoEAStream"),
                ChannelId = channel.Id,
                FileSize = 1,
                UploadedAt = DateTime.UtcNow
            });

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["VideoDownload:OutputPath"] = root })
                .Build();

            var additionalContentService = new AdditionalContentService(
                NullLogger<AdditionalContentService>.Instance,
                configuration,
                db.AdditionalContentRepository,
                db.ChannelRepository,
                db.PlaylistRepository,
                db.PlaylistVideoRepository,
                db.VideoRepository,
                db.PlaylistAdditionalContentRepository,
                db.VideoAdditionalContentRepository);

            var job = CreateScanJob(db, configuration, additionalContentService);
            await job.ExecuteAsync(channel.Id);

            Assert.True(File.Exists(videoPath));
            Assert.True(File.Exists(srtPath));
            Assert.True(File.Exists(extraPath));
            Assert.True(File.Exists(Path.ChangeExtension(srtPath, ".vtt")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenTrackedVideoHasNoFilePath_LinksFileFromDisk()
    {
        string root = Path.Combine(Path.GetTempPath(), "mva-scan-reimport-" + Guid.NewGuid().ToString("N"));
        string channelFolderName = "ReimportChannel";
        string playlistDir = Path.Combine(root, "_Custom", channelFolderName, "Course1");
        Directory.CreateDirectory(playlistDir);

        string videoPath = Path.Combine(playlistDir, "01 - Lesson.mp4");
        await File.WriteAllBytesAsync(videoPath, [0x00, 0x00, 0x00]);
        string videoId = "Course1/01 - Lesson";

        try
        {
            using var db = new InMemoryDatabaseFixture();
            var channel = await db.ChannelRepository.InsertAsync(new Channel
            {
                ChannelId = channelFolderName,
                Name = channelFolderName,
                Url = "custom://local",
                Platform = "Custom",
                SubscribedAt = DateTime.UtcNow
            });

            // Tracked row with no file path (or a missing path) must be re-linked when the file appears on disk.
            await db.VideoRepository.InsertAsync(new Video
            {
                VideoId = videoId,
                Title = "01 - Lesson",
                Url = videoPath,
                Platform = "Custom",
                ChannelId = channel.Id,
                FilePath = null,
                FileSize = null,
                DownloadedAt = DateTime.UtcNow
            });

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["VideoDownload:OutputPath"] = root })
                .Build();

            var result = await CreateScanJob(db, configuration).ExecuteAsync(channel.Id);

            var videos = (await db.VideoRepository.FindAsync(new SearchOptions<Video>
            {
                Query = x => x.ChannelId == channel.Id
            })).ToList();
            Assert.Single(videos);
            Assert.Equal(1, result.UpdatedVideos);
            Assert.True(File.Exists(videos[0].FilePath));
            Assert.Equal(Path.GetFullPath(videoPath), Path.GetFullPath(videos[0].FilePath!));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoDbRows_ImportsVideosFromCustomChannelFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), "mva-scan-new-" + Guid.NewGuid().ToString("N"));
        string channelFolderName = "NewImportChannel";
        string playlistDir = Path.Combine(root, "_Custom", channelFolderName, "Course1");
        Directory.CreateDirectory(playlistDir);
        string videoPath = Path.Combine(playlistDir, "01 - Lesson.mp4");
        await File.WriteAllBytesAsync(videoPath, [0x00, 0x00, 0x00]);

        try
        {
            using var db = new InMemoryDatabaseFixture();
            var channel = await db.ChannelRepository.InsertAsync(new Channel
            {
                ChannelId = channelFolderName,
                Name = channelFolderName,
                Url = "custom://local",
                Platform = "Custom",
                SubscribedAt = DateTime.UtcNow
            });

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["VideoDownload:OutputPath"] = root })
                .Build();

            var job = CreateScanJob(db, configuration);
            var result = await job.ExecuteAsync(channel.Id);

            Assert.Equal(1, result.NewVideos);
            var videos = (await db.VideoRepository.FindAsync(new SearchOptions<Video>
            {
                Query = x => x.ChannelId == channel.Id
            })).ToList();
            Assert.Single(videos);
            Assert.Equal(Path.GetFullPath(videoPath), Path.GetFullPath(videos[0].FilePath!));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenDownloadPathMissing_ReturnsEmptyResult()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), "mva-missing-" + Guid.NewGuid().ToString("N"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["VideoDownload:OutputPath"] = missingPath })
            .Build();

        var job = new FileSystemScanJob(
            NullLogger<FileSystemScanJob>.Instance,
            configuration,
            Mock.Of<IRepository<AdditionalContentItem>>(),
            Mock.Of<IRepository<Channel>>(),
            Mock.Of<IRepository<CustomPlaylistVideo>>(),
            Mock.Of<IRepository<Playlist>>(),
            Mock.Of<IRepository<PlaylistVideo>>(),
            Mock.Of<IRepository<Series>>(),
            Mock.Of<IRepository<SeriesPlaylist>>(),
            Mock.Of<IRepository<UserPlaylistVideo>>(),
            Mock.Of<IRepository<UserVideo>>(),
            Mock.Of<IRepository<Video>>(),
            Mock.Of<IRepository<VideoTag>>(),
            Mock.Of<IAdditionalContentService>(),
            new ThumbnailService(NullLogger<ThumbnailService>.Instance, Mock.Of<IHttpClientFactory>()));

        var result = await job.ExecuteAsync();

        Assert.Equal(0, result.NewVideos);
        Assert.Equal(0, result.UpdatedVideos);
    }
}