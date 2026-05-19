using System.Text;
using Microsoft.AspNetCore.Http;
using MyVideoArchive.Models.Requests.AdditionalContent;

namespace MyVideoArchive.Tests.Services;

public class AdditionalContentServiceTests
{
    private static AdditionalContentService CreateService(InMemoryDatabaseFixture db, IConfiguration? configuration = null) =>
        new(
            NullLogger<AdditionalContentService>.Instance,
            configuration ?? new ConfigurationBuilder().Build(),
            db.AdditionalContentRepository,
            db.ChannelRepository,
            db.PlaylistRepository,
            db.PlaylistVideoRepository,
            db.VideoRepository,
            db.PlaylistAdditionalContentRepository,
            db.VideoAdditionalContentRepository);

    private static IConfiguration ConfigWithDownloadRoot(string root) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["VideoDownload:OutputPath"] = root })
            .Build();

    private static IFormFile CreateFormFile(string fileName, string contentType, byte[] content)
    {
        var mock = new Mock<IFormFile>();
        mock.Setup(f => f.FileName).Returns(fileName);
        mock.Setup(f => f.ContentType).Returns(contentType);
        mock.Setup(f => f.Length).Returns(content.Length);
        mock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns((Stream dest, CancellationToken ct) => dest.WriteAsync(content.AsMemory(0, content.Length), ct).AsTask());
        return mock.Object;
    }

    [Fact]
    public async Task GetChannelItemsAsync_ReturnsItemsForChannel()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        await db.AdditionalContentRepository.InsertAsync(new AdditionalContentItem
        {
            FileName = "a.pdf",
            FilePath = @"C:\fake\a.pdf",
            ChannelId = channel.Id,
            FileSize = 1,
            UploadedAt = DateTime.UtcNow
        });

        var result = await CreateService(db).GetChannelItemsAsync(channel.Id);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("a.pdf", result.Value[0].FileName);
    }

    [Fact]
    public async Task GetItemsForVideoAsync_ReturnsLinkedItems()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        var video = await db.VideoRepository.InsertAsync(new Video
        {
            VideoId = "v1",
            Title = "V",
            Url = "https://v",
            Platform = "YT",
            ChannelId = channel.Id
        });
        var item = await db.AdditionalContentRepository.InsertAsync(new AdditionalContentItem
        {
            FileName = "x.txt",
            FilePath = @"C:\fake\x.txt",
            ChannelId = channel.Id,
            FileSize = 2,
            UploadedAt = DateTime.UtcNow
        });
        await db.VideoAdditionalContentRepository.InsertAsync(new VideoAdditionalContentItem
        {
            VideoId = video.Id,
            AdditionalContentItemId = item.Id
        });

        var result = await CreateService(db).GetItemsForVideoAsync(video.Id);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal(item.Id, result.Value[0].Id);
    }

    [Fact]
    public async Task GetAvailableItemsForVideoOnPlaylistAsync_WhenPlaylistMissing_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var result = await CreateService(db).GetAvailableItemsForVideoOnPlaylistAsync(1, 1);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task GetAvailableItemsForVideoOnPlaylistAsync_WhenVideoNotInPlaylist_ReturnsInvalid()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        var playlist = await db.PlaylistRepository.InsertAsync(new Playlist
        {
            PlaylistId = "pl",
            Name = "Pl",
            Url = "https://pl",
            Platform = "YT",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var video = await db.VideoRepository.InsertAsync(new Video
        {
            VideoId = "v1",
            Title = "V",
            Url = "https://v",
            Platform = "YT",
            ChannelId = channel.Id
        });

        var result = await CreateService(db).GetAvailableItemsForVideoOnPlaylistAsync(playlist.Id, video.Id);

        Assert.Equal(ResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task GetAvailableItemsForVideoOnPlaylistAsync_WhenVideoWrongChannel_ReturnsInvalid()
    {
        using var db = new InMemoryDatabaseFixture();
        var ch1 = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "c1",
            Name = "A",
            Url = "https://a",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        var ch2 = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "c2",
            Name = "B",
            Url = "https://b",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        var playlist = await db.PlaylistRepository.InsertAsync(new Playlist
        {
            PlaylistId = "pl",
            Name = "Pl",
            Url = "https://pl",
            Platform = "YT",
            ChannelId = ch1.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var video = await db.VideoRepository.InsertAsync(new Video
        {
            VideoId = "v1",
            Title = "V",
            Url = "https://v",
            Platform = "YT",
            ChannelId = ch2.Id
        });
        await db.PlaylistVideoRepository.InsertAsync(new PlaylistVideo
        {
            PlaylistId = playlist.Id,
            VideoId = video.Id,
            Order = 0
        });

        var result = await CreateService(db).GetAvailableItemsForVideoOnPlaylistAsync(playlist.Id, video.Id);

        Assert.Equal(ResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task GetAvailableItemsForVideoOnPlaylistAsync_ReturnsChannelItemsNotYetLinked()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        var playlist = await db.PlaylistRepository.InsertAsync(new Playlist
        {
            PlaylistId = "pl",
            Name = "Pl",
            Url = "https://pl",
            Platform = "YT",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var video = await db.VideoRepository.InsertAsync(new Video
        {
            VideoId = "v1",
            Title = "V",
            Url = "https://v",
            Platform = "YT",
            ChannelId = channel.Id
        });
        await db.PlaylistVideoRepository.InsertAsync(new PlaylistVideo
        {
            PlaylistId = playlist.Id,
            VideoId = video.Id,
            Order = 0
        });
        var availableItem = await db.AdditionalContentRepository.InsertAsync(new AdditionalContentItem
        {
            FileName = "extra.zip",
            FilePath = @"C:\fake\extra.zip",
            ChannelId = channel.Id,
            FileSize = 3,
            UploadedAt = DateTime.UtcNow
        });
        var alreadyLinked = await db.AdditionalContentRepository.InsertAsync(new AdditionalContentItem
        {
            FileName = "linked.pdf",
            FilePath = @"C:\fake\linked.pdf",
            ChannelId = channel.Id,
            FileSize = 4,
            UploadedAt = DateTime.UtcNow
        });
        await db.VideoAdditionalContentRepository.InsertAsync(new VideoAdditionalContentItem
        {
            VideoId = video.Id,
            AdditionalContentItemId = alreadyLinked.Id
        });

        var result = await CreateService(db).GetAvailableItemsForVideoOnPlaylistAsync(playlist.Id, video.Id);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal(availableItem.Id, result.Value[0].Id);
    }

    [Fact]
    public async Task GetAvailableItemsForVideoOnPlaylistAsync_WhenOnlyUnassignedInPlaylist_ExcludesItemsLinkedToOtherPlaylistVideo()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        var playlist = await db.PlaylistRepository.InsertAsync(new Playlist
        {
            PlaylistId = "pl",
            Name = "Pl",
            Url = "https://pl",
            Platform = "YT",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var video1 = await db.VideoRepository.InsertAsync(new Video
        {
            VideoId = "v1",
            Title = "V1",
            Url = "https://v1",
            Platform = "YT",
            ChannelId = channel.Id
        });
        var video2 = await db.VideoRepository.InsertAsync(new Video
        {
            VideoId = "v2",
            Title = "V2",
            Url = "https://v2",
            Platform = "YT",
            ChannelId = channel.Id
        });
        await db.PlaylistVideoRepository.InsertAsync(new PlaylistVideo
        {
            PlaylistId = playlist.Id,
            VideoId = video1.Id,
            Order = 0
        });
        await db.PlaylistVideoRepository.InsertAsync(new PlaylistVideo
        {
            PlaylistId = playlist.Id,
            VideoId = video2.Id,
            Order = 1
        });

        var unlinked = await db.AdditionalContentRepository.InsertAsync(new AdditionalContentItem
        {
            FileName = "free.zip",
            FilePath = @"C:\fake\free.zip",
            ChannelId = channel.Id,
            FileSize = 1,
            UploadedAt = DateTime.UtcNow
        });
        var linkedToV2 = await db.AdditionalContentRepository.InsertAsync(new AdditionalContentItem
        {
            FileName = "v2-only.pdf",
            FilePath = @"C:\fake\v2.pdf",
            ChannelId = channel.Id,
            FileSize = 2,
            UploadedAt = DateTime.UtcNow
        });
        await db.VideoAdditionalContentRepository.InsertAsync(new VideoAdditionalContentItem
        {
            VideoId = video2.Id,
            AdditionalContentItemId = linkedToV2.Id
        });

        var svc = CreateService(db);

        var allAvailable = await svc.GetAvailableItemsForVideoOnPlaylistAsync(playlist.Id, video1.Id, onlyUnassignedInPlaylist: false);
        Assert.True(allAvailable.IsSuccess);
        Assert.Equal(2, allAvailable.Value.Count);
        Assert.Contains(allAvailable.Value, i => i.Id == unlinked.Id);
        Assert.Contains(allAvailable.Value, i => i.Id == linkedToV2.Id);

        var filtered = await svc.GetAvailableItemsForVideoOnPlaylistAsync(playlist.Id, video1.Id, onlyUnassignedInPlaylist: true);
        Assert.True(filtered.IsSuccess);
        Assert.Single(filtered.Value);
        Assert.Equal(unlinked.Id, filtered.Value[0].Id);
    }

    [Fact]
    public async Task GetAvailableItemsForVideoAsync_ReturnsChannelItemsNotLinkedToVideo()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        var video = await db.VideoRepository.InsertAsync(new Video
        {
            VideoId = "v1",
            Title = "V1",
            Url = "https://v1",
            Platform = "YT",
            ChannelId = channel.Id
        });
        var available = await db.AdditionalContentRepository.InsertAsync(new AdditionalContentItem
        {
            FileName = "readme.txt",
            FilePath = @"C:\fake\readme.txt",
            ChannelId = channel.Id,
            FileSize = 1,
            UploadedAt = DateTime.UtcNow
        });
        var linked = await db.AdditionalContentRepository.InsertAsync(new AdditionalContentItem
        {
            FileName = "linked.pdf",
            FilePath = @"C:\fake\linked.pdf",
            ChannelId = channel.Id,
            FileSize = 2,
            UploadedAt = DateTime.UtcNow
        });
        await db.VideoAdditionalContentRepository.InsertAsync(new VideoAdditionalContentItem
        {
            VideoId = video.Id,
            AdditionalContentItemId = linked.Id
        });

        var result = await CreateService(db).GetAvailableItemsForVideoAsync(video.Id);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal(available.Id, result.Value[0].Id);
    }

    [Fact]
    public async Task LinkItemsToVideoAsync_WithoutPlaylist_LinksVideoAndChannelPlaylistsContainingVideo()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        var playlist = await db.PlaylistRepository.InsertAsync(new Playlist
        {
            PlaylistId = "pl",
            Name = "Pl",
            Url = "https://pl",
            Platform = "YT",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var video = await db.VideoRepository.InsertAsync(new Video
        {
            VideoId = "v1",
            Title = "V1",
            Url = "https://v1",
            Platform = "YT",
            ChannelId = channel.Id
        });
        await db.PlaylistVideoRepository.InsertAsync(new PlaylistVideo
        {
            PlaylistId = playlist.Id,
            VideoId = video.Id,
            Order = 0
        });
        var item = await db.AdditionalContentRepository.InsertAsync(new AdditionalContentItem
        {
            FileName = "extra.zip",
            FilePath = @"C:\fake\extra.zip",
            ChannelId = channel.Id,
            FileSize = 1,
            UploadedAt = DateTime.UtcNow
        });

        var svc = CreateService(db);
        var linkResult = await svc.LinkItemsToVideoAsync(video.Id, new LinkAdditionalContentToVideoRequest([item.Id]));

        Assert.True(linkResult.IsSuccess);
        var videoLinks = await db.VideoAdditionalContentRepository.FindAsync(new SearchOptions<VideoAdditionalContentItem>
        {
            Query = x => x.VideoId == video.Id
        });
        Assert.Single(videoLinks);
        var playlistLinks = await db.PlaylistAdditionalContentRepository.FindAsync(new SearchOptions<PlaylistAdditionalContentItem>
        {
            Query = x => x.PlaylistId == playlist.Id && x.AdditionalContentItemId == item.Id
        });
        Assert.Single(playlistLinks);
    }

    [Fact]
    public async Task UploadAsync_WhenChannelMissing_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        string root = Path.Combine(Path.GetTempPath(), "mva-ac-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = CreateFormFile("a.txt", "text/plain", Encoding.UTF8.GetBytes("hi"));
            var result = await CreateService(db, ConfigWithDownloadRoot(root)).UploadAsync(999, file, null);
            Assert.Equal(ResultStatus.NotFound, result.Status);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task UploadAsync_WritesFileAndPersistsItem()
    {
        using var db = new InMemoryDatabaseFixture();
        string root = Path.Combine(Path.GetTempPath(), "mva-ac-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var channel = await db.ChannelRepository.InsertAsync(new Channel
            {
                ChannelId = "UCupload",
                Name = "C",
                Url = "https://c",
                Platform = "YT",
                SubscribedAt = DateTime.UtcNow
            });
            var file = CreateFormFile("readme.txt", "text/plain", Encoding.UTF8.GetBytes("content"));
            var result = await CreateService(db, ConfigWithDownloadRoot(root)).UploadAsync(channel.Id, file, null);

            Assert.True(result.IsSuccess);
            var row = await db.AdditionalContentRepository.FindOneAsync(result.Value.Id);
            Assert.NotNull(row);
            Assert.True(File.Exists(row!.FilePath));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task UploadAsync_WhenPlaylistIdsInvalid_ReturnsInvalid()
    {
        using var db = new InMemoryDatabaseFixture();
        string root = Path.Combine(Path.GetTempPath(), "mva-ac-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var channel = await db.ChannelRepository.InsertAsync(new Channel
            {
                ChannelId = "ch1",
                Name = "C",
                Url = "https://c",
                Platform = "YT",
                SubscribedAt = DateTime.UtcNow
            });
            var otherChannel = await db.ChannelRepository.InsertAsync(new Channel
            {
                ChannelId = "ch2",
                Name = "D",
                Url = "https://d",
                Platform = "YT",
                SubscribedAt = DateTime.UtcNow
            });
            var foreignPl = await db.PlaylistRepository.InsertAsync(new Playlist
            {
                PlaylistId = "plf",
                Name = "Foreign",
                Url = "https://plf",
                Platform = "YT",
                ChannelId = otherChannel.Id,
                SubscribedAt = DateTime.UtcNow
            });
            var file = CreateFormFile("a.txt", "text/plain", Encoding.UTF8.GetBytes("x"));
            var result = await CreateService(db, ConfigWithDownloadRoot(root)).UploadAsync(channel.Id, file, [foreignPl.Id]);
            Assert.Equal(ResultStatus.Invalid, result.Status);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task UpdateAsync_WhenItemMissing_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var result = await CreateService(db).UpdateAsync(999, new UpdateAdditionalContentRequest("n", []));
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesFileNameAndPlaylistLinks()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        var pl1 = await db.PlaylistRepository.InsertAsync(new Playlist
        {
            PlaylistId = "p1",
            Name = "P1",
            Url = "https://p1",
            Platform = "YT",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var pl2 = await db.PlaylistRepository.InsertAsync(new Playlist
        {
            PlaylistId = "p2",
            Name = "P2",
            Url = "https://p2",
            Platform = "YT",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var item = await db.AdditionalContentRepository.InsertAsync(new AdditionalContentItem
        {
            FileName = "old.txt",
            FilePath = @"C:\noop\old.txt",
            ChannelId = channel.Id,
            FileSize = 1,
            UploadedAt = DateTime.UtcNow
        });
        await db.PlaylistAdditionalContentRepository.InsertAsync(new PlaylistAdditionalContentItem
        {
            PlaylistId = pl1.Id,
            AdditionalContentItemId = item.Id
        });

        var result = await CreateService(db).UpdateAsync(item.Id, new UpdateAdditionalContentRequest("new.txt", [pl2.Id]));

        Assert.True(result.IsSuccess);
        var reloaded = await db.AdditionalContentRepository.FindOneAsync(item.Id);
        Assert.Equal("new.txt", reloaded!.FileName);
        var links = await db.PlaylistAdditionalContentRepository.FindAsync(new SearchOptions<PlaylistAdditionalContentItem>
        {
            Query = x => x.AdditionalContentItemId == item.Id
        });
        var linksList = links.ToList();
        Assert.Single(linksList);
        Assert.Equal(pl2.Id, linksList[0].PlaylistId);
    }

    [Fact]
    public async Task DeleteAsync_WhenMissing_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var result = await CreateService(db).DeleteAsync(999);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task DeleteAsync_DeletesFileAndRow()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        string tempFile = Path.Combine(Path.GetTempPath(), "mva-del-" + Guid.NewGuid().ToString("N") + ".bin");
        await File.WriteAllBytesAsync(tempFile, [1, 2, 3]);
        var item = await db.AdditionalContentRepository.InsertAsync(new AdditionalContentItem
        {
            FileName = "f.bin",
            FilePath = tempFile,
            ChannelId = channel.Id,
            FileSize = 3,
            UploadedAt = DateTime.UtcNow
        });

        var result = await CreateService(db).DeleteAsync(item.Id);

        Assert.True(result.IsSuccess);
        Assert.False(File.Exists(tempFile));
        Assert.Null(await db.AdditionalContentRepository.FindOneAsync(item.Id));
    }

    [Fact]
    public async Task GetDownloadInfoAsync_WhenFileMissing_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        var item = await db.AdditionalContentRepository.InsertAsync(new AdditionalContentItem
        {
            FileName = "ghost.bin",
            FilePath = Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid().ToString("N") + ".bin"),
            ChannelId = channel.Id,
            FileSize = 0,
            UploadedAt = DateTime.UtcNow
        });

        var result = await CreateService(db).GetDownloadInfoAsync(item.Id);

        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task GetDownloadInfoAsync_WhenFileExists_AppendsStoredExtensionToDisplayName()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        string path = Path.Combine(Path.GetTempPath(), "mva-dl-" + Guid.NewGuid().ToString("N") + ".pdf");
        await File.WriteAllTextAsync(path, "x");
        try
        {
            var item = await db.AdditionalContentRepository.InsertAsync(new AdditionalContentItem
            {
                FileName = "report",
                FilePath = path,
                ContentType = "application/pdf",
                ChannelId = channel.Id,
                FileSize = 1,
                UploadedAt = DateTime.UtcNow
            });

            var result = await CreateService(db).GetDownloadInfoAsync(item.Id);

            Assert.True(result.IsSuccess);
            Assert.EndsWith(".pdf", result.Value.DownloadFileName, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(path, result.Value.PhysicalPath);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task LinkItemsToVideoAsync_WhenEmptyItemIds_ReturnsSuccess()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        var playlist = await db.PlaylistRepository.InsertAsync(new Playlist
        {
            PlaylistId = "pl",
            Name = "Pl",
            Url = "https://pl",
            Platform = "YT",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var video = await db.VideoRepository.InsertAsync(new Video
        {
            VideoId = "v1",
            Title = "V",
            Url = "https://v",
            Platform = "YT",
            ChannelId = channel.Id
        });
        await db.PlaylistVideoRepository.InsertAsync(new PlaylistVideo
        {
            PlaylistId = playlist.Id,
            VideoId = video.Id,
            Order = 0
        });
        await db.AdditionalContentRepository.InsertAsync(new AdditionalContentItem
        {
            FileName = "e.txt",
            FilePath = @"C:\fake\e.txt",
            ChannelId = channel.Id,
            FileSize = 1,
            UploadedAt = DateTime.UtcNow
        });

        var result = await CreateService(db).LinkItemsToVideoAsync(
            video.Id,
            playlist.Id,
            new LinkAdditionalContentToVideoRequest([]));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task UnlinkItemFromVideoAsync_WhenMissing_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var result = await CreateService(db).UnlinkItemFromVideoAsync(1, 1);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task ImportFileAsync_SkipsWhenPathAlreadyRegistered()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        string path = Path.Combine(Path.GetTempPath(), "mva-imp-" + Guid.NewGuid().ToString("N") + ".dat");
        await File.WriteAllTextAsync(path, "z");
        try
        {
            await db.AdditionalContentRepository.InsertAsync(new AdditionalContentItem
            {
                FileName = Path.GetFileName(path),
                FilePath = path,
                ChannelId = channel.Id,
                FileSize = 1,
                UploadedAt = DateTime.UtcNow
            });
            var before = await db.AdditionalContentRepository.FindAsync(new SearchOptions<AdditionalContentItem>
            {
                Query = x => x.FilePath == path
            });

            Assert.False(await CreateService(db).ImportFileAsync(path, channel.Id, null));

            var after = await db.AdditionalContentRepository.FindAsync(new SearchOptions<AdditionalContentItem>
            {
                Query = x => x.FilePath == path
            });
            Assert.Equal(before.Count, after.Count);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task ImportFileAsync_InsertsRowAndOptionalPlaylistLink()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "ch1",
            Name = "C",
            Url = "https://c",
            Platform = "YT",
            SubscribedAt = DateTime.UtcNow
        });
        var playlist = await db.PlaylistRepository.InsertAsync(new Playlist
        {
            PlaylistId = "pl",
            Name = "Pl",
            Url = "https://pl",
            Platform = "YT",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        string path = Path.Combine(Path.GetTempPath(), "mva-imp2-" + Guid.NewGuid().ToString("N") + ".md");
        await File.WriteAllTextAsync(path, "# doc");
        try
        {
            Assert.True(await CreateService(db).ImportFileAsync(path, channel.Id, playlist.Id));

            var items = await db.AdditionalContentRepository.FindAsync(new SearchOptions<AdditionalContentItem>
            {
                Query = x => x.FilePath == path
            });
            var itemsList = items.ToList();
            Assert.Single(itemsList);
            var links = await db.PlaylistAdditionalContentRepository.FindAsync(new SearchOptions<PlaylistAdditionalContentItem>
            {
                Query = x => x.AdditionalContentItemId == itemsList[0].Id && x.PlaylistId == playlist.Id
            });
            Assert.Single(links);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task ImportFileAsync_WithVideoId_LinksVideoAndPlaylistsContainingVideo()
    {
        using var db = new InMemoryDatabaseFixture();
        var channel = await db.ChannelRepository.InsertAsync(new Channel
        {
            ChannelId = "c",
            Name = "C",
            Url = "custom://c",
            Platform = "Custom",
            SubscribedAt = DateTime.UtcNow
        });
        var playlist = await db.PlaylistRepository.InsertAsync(new Playlist
        {
            PlaylistId = "p1",
            Name = "P",
            Url = "custom://p1",
            Platform = "Custom",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var video = await db.VideoRepository.InsertAsync(new Video
        {
            VideoId = "MyLesson01",
            Title = "Lesson",
            Url = "x",
            Platform = "Custom",
            ChannelId = channel.Id
        });
        await db.PlaylistVideoRepository.InsertAsync(new PlaylistVideo
        {
            PlaylistId = playlist.Id,
            VideoId = video.Id,
            Order = 0
        });

        string path = Path.Combine(Path.GetTempPath(), "mva-vid-extra-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(path, "extra");
        try
        {
            Assert.True(await CreateService(db).ImportFileAsync(path, channel.Id, null, video.Id));

            var items = await db.AdditionalContentRepository.FindAsync(new SearchOptions<AdditionalContentItem>
            {
                Query = x => x.FilePath == path
            });
            var list = items.ToList();
            Assert.Single(list);
            int itemId = list[0].Id;

            var vLinks = (await db.VideoAdditionalContentRepository.FindAsync(new SearchOptions<VideoAdditionalContentItem>
            {
                Query = x => x.AdditionalContentItemId == itemId
            })).ToList();
            Assert.Single(vLinks);
            Assert.Equal(video.Id, vLinks[0].VideoId);

            var pLinks = (await db.PlaylistAdditionalContentRepository.FindAsync(new SearchOptions<PlaylistAdditionalContentItem>
            {
                Query = x => x.AdditionalContentItemId == itemId
            })).ToList();
            Assert.Single(pLinks);
            Assert.Equal(playlist.Id, pLinks[0].PlaylistId);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}