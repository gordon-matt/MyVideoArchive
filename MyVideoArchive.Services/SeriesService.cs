using Ardalis.Result;
using Microsoft.EntityFrameworkCore;
using MyVideoArchive.Models.Requests.Series;

namespace MyVideoArchive.Services;

public class SeriesService : ISeriesService
{
    private readonly IRepository<Series> seriesRepository;
    private readonly IRepository<SeriesPlaylist> seriesPlaylistRepository;
    private readonly IRepository<Playlist> playlistRepository;
    private readonly IRepository<Channel> channelRepository;
    private readonly ILogger<SeriesService> logger;

    public SeriesService(
        IRepository<Series> seriesRepository,
        IRepository<SeriesPlaylist> seriesPlaylistRepository,
        IRepository<Playlist> playlistRepository,
        IRepository<Channel> channelRepository,
        ILogger<SeriesService> logger)
    {
        this.seriesRepository = seriesRepository;
        this.seriesPlaylistRepository = seriesPlaylistRepository;
        this.playlistRepository = playlistRepository;
        this.channelRepository = channelRepository;
        this.logger = logger;
    }

    public async Task<Result<IList<SeriesDto>>> GetSeriesForChannelAsync(int channelId, CancellationToken cancellationToken = default)
    {
        var seriesList = await seriesRepository.FindAsync(new SearchOptions<Series>
        {
            CancellationToken = cancellationToken,
            Query = x => x.ChannelId == channelId,
            Include = q => q
                .Include(s => s.SeriesPlaylists)
                    .ThenInclude(sp => sp.Playlist)
        });

        var dtos = seriesList
            .Select(s => ToDto(s))
            .ToList();

        return Result.Success<IList<SeriesDto>>(dtos);
    }

    public async Task<Result<SeriesDto>> GetSeriesAsync(int seriesId, CancellationToken cancellationToken = default)
    {
        var series = await seriesRepository.FindOneAsync(new SearchOptions<Series>
        {
            CancellationToken = cancellationToken,
            Query = x => x.Id == seriesId,
            Include = q => q
                .Include(s => s.SeriesPlaylists)
                    .ThenInclude(sp => sp.Playlist)
        });

        if (series is null)
        {
            return Result.NotFound();
        }

        return Result.Success(ToDto(series));
    }

    public async Task<Result<SeriesDto>> CreateSeriesAsync(int channelId, CreateSeriesRequest request, CancellationToken cancellationToken = default)
    {
        var channel = await channelRepository.FindOneAsync(channelId);
        if (channel is null)
        {
            return Result.NotFound("Channel not found.");
        }

        var series = new Series
        {
            Name = request.Name,
            ChannelId = channelId
        };

        await seriesRepository.InsertAsync(series, ContextOptions.ForCancellationToken(cancellationToken));
        return Result.Success(new SeriesDto(series.Id, series.Name, series.ChannelId, []));
    }

    public async Task<Result> UpdateSeriesAsync(int seriesId, UpdateSeriesRequest request, CancellationToken cancellationToken = default)
    {
        var series = await seriesRepository.FindOneAsync(seriesId);
        if (series is null)
        {
            return Result.NotFound();
        }

        series.Name = request.Name;
        await seriesRepository.UpdateAsync(series, ContextOptions.ForCancellationToken(cancellationToken));
        return Result.Success();
    }

    public async Task<Result> UpdateSeriesPlaylistsAsync(int seriesId, UpdateSeriesPlaylistsRequest request, CancellationToken cancellationToken = default)
    {
        var series = await seriesRepository.FindOneAsync(seriesId);
        if (series is null)
        {
            return Result.NotFound();
        }

        if (request.PlaylistIds.Count > 0)
        {
            var playlists = await playlistRepository.FindAsync(new SearchOptions<Playlist>
            {
                CancellationToken = cancellationToken,
                Query = x => request.PlaylistIds.Contains(x.Id)
            });

            if (playlists.Any(p => p.ChannelId != series.ChannelId))
            {
                return Result.Invalid([new ValidationError("playlistIds", "All playlists must belong to the series channel.")]);
            }
        }

        var existing = await seriesPlaylistRepository.FindAsync(new SearchOptions<SeriesPlaylist>
        {
            CancellationToken = cancellationToken,
            Query = x => x.SeriesId == seriesId
        });

        if (existing.Count > 0)
        {
            await seriesPlaylistRepository.DeleteAsync(existing, ContextOptions.ForCancellationToken(cancellationToken));
        }

        if (request.PlaylistIds.Count > 0)
        {
            var inserts = request.PlaylistIds
                .Select((playlistId, index) => new SeriesPlaylist
                {
                    SeriesId = seriesId,
                    PlaylistId = playlistId,
                    SortOrder = index
                })
                .ToList();

            await seriesPlaylistRepository.InsertAsync(inserts, ContextOptions.ForCancellationToken(cancellationToken));
        }

        return Result.Success();
    }

    public async Task<Result> DeleteSeriesAsync(int seriesId, CancellationToken cancellationToken = default)
    {
        var series = await seriesRepository.FindOneAsync(seriesId);
        if (series is null)
        {
            return Result.NotFound();
        }

        await seriesRepository.DeleteAsync(series, ContextOptions.ForCancellationToken(cancellationToken));
        return Result.Success();
    }

    public async Task<Result<IList<PlaylistSeriesDto>>> GetSeriesForPlaylistAsync(int playlistId, CancellationToken cancellationToken = default)
    {
        var junctions = await seriesPlaylistRepository.FindAsync(new SearchOptions<SeriesPlaylist>
        {
            CancellationToken = cancellationToken,
            Query = x => x.PlaylistId == playlistId,
            Include = q => q.Include(sp => sp.Series)
        });

        var dtos = junctions
            .Select(j => new PlaylistSeriesDto(j.Series.Id, j.Series.Name))
            .ToList();

        return Result.Success<IList<PlaylistSeriesDto>>(dtos);
    }

    private static SeriesDto ToDto(Series series) => new(
        series.Id,
        series.Name,
        series.ChannelId,
        series.SeriesPlaylists
            .OrderBy(sp => sp.SortOrder)
            .Select(sp => new SeriesPlaylistDto(
                sp.PlaylistId,
                sp.Playlist?.Name ?? string.Empty,
                sp.Playlist?.ThumbnailUrl,
                sp.SortOrder))
            .ToList());
}
