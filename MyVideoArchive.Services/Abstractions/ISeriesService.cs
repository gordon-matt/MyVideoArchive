using Ardalis.Result;
using MyVideoArchive.Models.Requests.Series;

namespace MyVideoArchive.Services;

public interface ISeriesService
{
    Task<Result<IList<SeriesDto>>> GetSeriesForChannelAsync(int channelId, CancellationToken cancellationToken = default);

    Task<Result<SeriesDto>> GetSeriesAsync(int seriesId, CancellationToken cancellationToken = default);

    Task<Result<SeriesDto>> CreateSeriesAsync(int channelId, CreateSeriesRequest request, CancellationToken cancellationToken = default);

    Task<Result> UpdateSeriesAsync(int seriesId, UpdateSeriesRequest request, CancellationToken cancellationToken = default);

    Task<Result> UpdateSeriesPlaylistsAsync(int seriesId, UpdateSeriesPlaylistsRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteSeriesAsync(int seriesId, CancellationToken cancellationToken = default);

    Task<Result<IList<PlaylistSeriesDto>>> GetSeriesForPlaylistAsync(int playlistId, CancellationToken cancellationToken = default);
}

public record SeriesDto(int Id, string Name, int ChannelId, List<SeriesPlaylistDto> Playlists);

public record SeriesPlaylistDto(int Id, string Name, string? ThumbnailUrl, int SortOrder);

public record PlaylistSeriesDto(int Id, string Name);
