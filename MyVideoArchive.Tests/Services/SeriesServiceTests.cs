using MyVideoArchive.Models.Requests.Series;

namespace MyVideoArchive.Tests.Services;

public class SeriesServiceTests
{
    private static SeriesService CreateService(InMemoryDatabaseFixture db) =>
        new(
            db.SeriesRepository,
            db.SeriesPlaylistRepository,
            db.PlaylistRepository,
            db.ChannelRepository,
            NullLogger<SeriesService>.Instance);

    [Fact]
    public async Task GetSeriesForChannelAsync_ReturnsOrderedDtos()
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
            Name = "Zebra",
            Url = "https://p1",
            Platform = "YT",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var pl2 = await db.PlaylistRepository.InsertAsync(new Playlist
        {
            PlaylistId = "p2",
            Name = "Alpha",
            Url = "https://p2",
            Platform = "YT",
            ChannelId = channel.Id,
            SubscribedAt = DateTime.UtcNow
        });
        var series = await db.SeriesRepository.InsertAsync(new Series { Name = "S1", ChannelId = channel.Id });
        await db.SeriesPlaylistRepository.InsertAsync([
            new SeriesPlaylist { SeriesId = series.Id, PlaylistId = pl1.Id, SortOrder = 1 },
            new SeriesPlaylist { SeriesId = series.Id, PlaylistId = pl2.Id, SortOrder = 0 }
        ]);

        var service = CreateService(db);
        var result = await service.GetSeriesForChannelAsync(channel.Id);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        var dto = result.Value[0];
        Assert.Equal(series.Id, dto.Id);
        Assert.Equal(2, dto.Playlists.Count);
        Assert.Equal(pl2.Id, dto.Playlists[0].Id);
        Assert.Equal(pl1.Id, dto.Playlists[1].Id);
    }

    [Fact]
    public async Task GetSeriesAsync_WhenMissing_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var result = await CreateService(db).GetSeriesAsync(999);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task CreateSeriesAsync_WhenChannelMissing_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var result = await CreateService(db).CreateSeriesAsync(999, new CreateSeriesRequest("S"));
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task CreateSeriesAsync_WhenChannelExists_ReturnsDto()
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
        var result = await CreateService(db).CreateSeriesAsync(channel.Id, new CreateSeriesRequest("New"));
        Assert.True(result.IsSuccess);
        Assert.Equal("New", result.Value.Name);
        Assert.Equal(channel.Id, result.Value.ChannelId);
        Assert.Empty(result.Value.Playlists);
    }

    [Fact]
    public async Task UpdateSeriesAsync_WhenMissing_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var result = await CreateService(db).UpdateSeriesAsync(999, new UpdateSeriesRequest("X"));
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task UpdateSeriesAsync_UpdatesName()
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
        var series = await db.SeriesRepository.InsertAsync(new Series { Name = "Old", ChannelId = channel.Id });
        var result = await CreateService(db).UpdateSeriesAsync(series.Id, new UpdateSeriesRequest("Renamed"));
        Assert.True(result.IsSuccess);
        var reloaded = await db.SeriesRepository.FindOneAsync(series.Id);
        Assert.Equal("Renamed", reloaded!.Name);
    }

    [Fact]
    public async Task UpdateSeriesPlaylistsAsync_WhenPlaylistFromOtherChannel_ReturnsInvalid()
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
        var series = await db.SeriesRepository.InsertAsync(new Series { Name = "S", ChannelId = ch1.Id });
        var foreignPl = await db.PlaylistRepository.InsertAsync(new Playlist
        {
            PlaylistId = "pf",
            Name = "Foreign",
            Url = "https://pf",
            Platform = "YT",
            ChannelId = ch2.Id,
            SubscribedAt = DateTime.UtcNow
        });

        var result = await CreateService(db).UpdateSeriesPlaylistsAsync(
            series.Id,
            new UpdateSeriesPlaylistsRequest { PlaylistIds = [foreignPl.Id] });

        Assert.Equal(ResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task UpdateSeriesPlaylistsAsync_ReplacesLinks()
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
        var series = await db.SeriesRepository.InsertAsync(new Series { Name = "S", ChannelId = channel.Id });
        await db.SeriesPlaylistRepository.InsertAsync([
            new SeriesPlaylist { SeriesId = series.Id, PlaylistId = pl1.Id, SortOrder = 0 }
        ]);

        var result = await CreateService(db).UpdateSeriesPlaylistsAsync(
            series.Id,
            new UpdateSeriesPlaylistsRequest { PlaylistIds = [pl2.Id, pl1.Id] });

        Assert.True(result.IsSuccess);
        var links = await db.SeriesPlaylistRepository.FindAsync(new SearchOptions<SeriesPlaylist>
        {
            Query = x => x.SeriesId == series.Id
        });
        Assert.Equal(2, links.Count);
        Assert.Equal(pl2.Id, links.Single(l => l.SortOrder == 0).PlaylistId);
        Assert.Equal(pl1.Id, links.Single(l => l.SortOrder == 1).PlaylistId);
    }

    [Fact]
    public async Task DeleteSeriesAsync_WhenMissing_ReturnsNotFound()
    {
        using var db = new InMemoryDatabaseFixture();
        var result = await CreateService(db).DeleteSeriesAsync(999);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task DeleteSeriesAsync_RemovesSeries()
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
        var series = await db.SeriesRepository.InsertAsync(new Series { Name = "S", ChannelId = channel.Id });
        var result = await CreateService(db).DeleteSeriesAsync(series.Id);
        Assert.True(result.IsSuccess);
        Assert.Null(await db.SeriesRepository.FindOneAsync(series.Id));
    }

    [Fact]
    public async Task GetSeriesForPlaylistAsync_ReturnsSeriesNames()
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
        var series = await db.SeriesRepository.InsertAsync(new Series { Name = "Arc", ChannelId = channel.Id });
        await db.SeriesPlaylistRepository.InsertAsync(new SeriesPlaylist
        {
            SeriesId = series.Id,
            PlaylistId = playlist.Id,
            SortOrder = 0
        });

        var result = await CreateService(db).GetSeriesForPlaylistAsync(playlist.Id);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal(series.Id, result.Value[0].Id);
        Assert.Equal("Arc", result.Value[0].Name);
    }
}