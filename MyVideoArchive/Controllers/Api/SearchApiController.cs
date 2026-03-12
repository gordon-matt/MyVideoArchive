using Extenso.Collections.Generic;
using LinqKit;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// Provides cross-entity search across channels, playlists and videos.
/// </summary>
[Authorize]
[ApiController]
[Route("api/search")]
public class SearchApiController : ControllerBase
{
    private readonly IUserContextService userContextService;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<Playlist> playlistRepository;
    private readonly IRepository<Video> videoRepository;
    private readonly IRepository<UserChannel> userChannelRepository;
    private readonly IRepository<ChannelTag> channelTagRepository;
    private readonly IRepository<PlaylistTag> playlistTagRepository;
    private readonly IRepository<VideoTag> videoTagRepository;
    private readonly IRepository<UserVideo> userVideoRepository;
    private readonly IRepository<CustomPlaylist> customPlaylistRepository;

    public SearchApiController(
        IUserContextService userContextService,
        IRepository<Channel> channelRepository,
        IRepository<Playlist> playlistRepository,
        IRepository<Video> videoRepository,
        IRepository<UserChannel> userChannelRepository,
        IRepository<ChannelTag> channelTagRepository,
        IRepository<PlaylistTag> playlistTagRepository,
        IRepository<VideoTag> videoTagRepository,
        IRepository<UserVideo> userVideoRepository,
        IRepository<CustomPlaylist> customPlaylistRepository)
    {
        this.userContextService = userContextService;
        this.channelRepository = channelRepository;
        this.playlistRepository = playlistRepository;
        this.videoRepository = videoRepository;
        this.userChannelRepository = userChannelRepository;
        this.channelTagRepository = channelTagRepository;
        this.playlistTagRepository = playlistTagRepository;
        this.videoTagRepository = videoTagRepository;
        this.userVideoRepository = userVideoRepository;
        this.customPlaylistRepository = customPlaylistRepository;
    }

    /// <summary>
    /// Search across channels, playlists and videos by title/name and/or tag.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] string? tag,
        [FromQuery] int channelPage = 1,
        [FromQuery] int playlistPage = 1,
        [FromQuery] int videoPage = 1,
        [FromQuery] int pageSize = 18,
        CancellationToken cancellationToken = default)
    {
        string? userId = userContextService.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        bool isAdmin = userContextService.IsAdministrator();
        string? searchLower = string.IsNullOrWhiteSpace(q) ? null : q.Trim().ToLowerInvariant();
        string? tagLower = string.IsNullOrWhiteSpace(tag) ? null : tag.Trim().ToLowerInvariant();

        // ── Channels ─────────────────────────────────────────────────────────

        IPagedCollection<int> accessibleChannelIds;
        if (isAdmin)
        {
            accessibleChannelIds = await channelRepository.FindAsync(
                new SearchOptions<Channel> { CancellationToken = cancellationToken },
                x => x.Id);
        }
        else
        {
            accessibleChannelIds = await userChannelRepository.FindAsync(
                new SearchOptions<UserChannel>
                {
                    CancellationToken = cancellationToken,
                    Query = x => x.UserId == userId
                },
                x => x.ChannelId);
        }

        var channelPredicate = PredicateBuilder.New<Channel>(
            x => accessibleChannelIds.Contains(x.Id));

        if (searchLower != null)
        {
            channelPredicate = channelPredicate.And(x => x.Name.ToLower().Contains(searchLower));
        }

        if (tagLower != null)
        {
            var channelIdsWithTag = await channelTagRepository.FindAsync(
                new SearchOptions<ChannelTag>
                {
                    CancellationToken = cancellationToken,
                    Query = x => x.Tag.Name.ToLower() == tagLower,
                    Include = q => q.Include(x => x.Tag)
                },
                x => x.ChannelId);

            var taggedChannelIds = channelIdsWithTag.ToHashSet();
            channelPredicate = channelPredicate.And(x => taggedChannelIds.Contains(x.Id));
        }

        var channels = await channelRepository.FindAsync(new SearchOptions<Channel>
        {
            CancellationToken = cancellationToken,
            Query = channelPredicate,
            OrderBy = q => q.OrderBy(x => x.Name),
            PageNumber = channelPage,
            PageSize = pageSize
        }, x => new
        {
            x.Id,
            x.Name,
            x.BannerUrl,
            x.AvatarUrl,
            x.Platform,
            x.SubscriberCount,
            x.VideoCount
        });

        // ── Playlists ─────────────────────────────────────────────────────────

        var playlistPredicate = PredicateBuilder.New<Playlist>(
            x => accessibleChannelIds.Contains(x.ChannelId) && !x.IsIgnored);

        if (searchLower != null)
        {
            playlistPredicate = playlistPredicate.And(x => x.Name.ToLower().Contains(searchLower));
        }

        if (tagLower != null)
        {
            var playlistIdsWithTag = await playlistTagRepository.FindAsync(
                new SearchOptions<PlaylistTag>
                {
                    CancellationToken = cancellationToken,
                    Query = x => x.Tag.Name.ToLower() == tagLower,
                    Include = q => q.Include(x => x.Tag)
                },
                x => x.PlaylistId);

            var taggedPlaylistIds = playlistIdsWithTag.ToHashSet();
            playlistPredicate = playlistPredicate.And(x => taggedPlaylistIds.Contains(x.Id));
        }

        // Fetch all matching channel playlists without DB-level pagination so they can be merged
        // with custom playlists in memory before paginating.
        var rawChannelPlaylists = await playlistRepository.FindAsync(new SearchOptions<Playlist>
        {
            CancellationToken = cancellationToken,
            Query = playlistPredicate,
            Include = q => q.Include(x => x.Channel)
        }, x => new
        {
            x.Id,
            x.PlaylistId,
            x.Name,
            x.ThumbnailUrl,
            x.Platform,
            x.VideoCount,
            ChannelName = x.Channel.Name,
            ChannelId = (int?)x.Channel.Id
        });

        var channelPlaylistItems = rawChannelPlaylists
            .Select(x => new PlaylistSearchResult(x.Id, x.PlaylistId, x.Name, x.ThumbnailUrl, x.Platform, x.VideoCount, x.ChannelName, x.ChannelId, false))
            .ToList();

        // Custom playlists are user-specific and don't support tags; skip them when filtering by tag.
        var customPlaylistItems = new List<PlaylistSearchResult>();
        if (tagLower == null)
        {
            var rawCustomPlaylists = await customPlaylistRepository.FindAsync(new SearchOptions<CustomPlaylist>
            {
                CancellationToken = cancellationToken,
                Query = x => x.UserId == userId && (searchLower == null || x.Name.ToLower().Contains(searchLower))
            }, x => new
            {
                x.Id,
                x.Name,
                x.ThumbnailUrl
            });

            customPlaylistItems = rawCustomPlaylists
                .Select(x => new PlaylistSearchResult(x.Id, null, x.Name, x.ThumbnailUrl, "Custom", null, null, null, true))
                .ToList();
        }

        // Merge both sources, sort alphabetically, and paginate in memory.
        var allPlaylistsSorted = channelPlaylistItems
            .Concat(customPlaylistItems)
            .OrderBy(x => x.Name)
            .ToList();

        int playlistsTotalCount = allPlaylistsSorted.Count;
        int playlistsTotalPages = pageSize > 0 ? (int)Math.Ceiling((double)playlistsTotalCount / pageSize) : 1;
        var playlistsPage = allPlaylistsSorted
            .Skip((playlistPage - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        // ── Videos ────────────────────────────────────────────────────────────

        var videoPredicate = PredicateBuilder.New<Video>(false);

        if (isAdmin)
        {
            videoPredicate = PredicateBuilder.New<Video>(x => x.DownloadedAt != null && !x.IsIgnored);
        }
        else
        {
            var userIgnoredVideoIds = (await userVideoRepository.FindAsync(
                new SearchOptions<UserVideo>
                {
                    CancellationToken = cancellationToken,
                    Query = x => x.UserId == userId && x.IsIgnored
                },
                x => x.VideoId)).ToHashSet();

            if (accessibleChannelIds.Count > 0)
            {
                var accessibleList = accessibleChannelIds.ToList();
                videoPredicate = videoPredicate.Or(x =>
                    accessibleList.Contains(x.ChannelId) &&
                    x.DownloadedAt != null &&
                    !x.IsIgnored &&
                    !userIgnoredVideoIds.Contains(x.Id));
            }

            if (!videoPredicate.IsStarted)
            {
                return Ok(new
                {
                    channels = new { items = channels, totalCount = channels.ItemCount, totalPages = channels.PageCount },
                    playlists = new { items = playlistsPage, totalCount = playlistsTotalCount, totalPages = playlistsTotalPages },
                    videos = new { items = Array.Empty<object>(), totalCount = 0, totalPages = 0 }
                });
            }
        }

        if (searchLower != null)
        {
            videoPredicate = videoPredicate.And(x =>
                x.Title.ToLower().Contains(searchLower) ||
                x.Channel.Name.ToLower().Contains(searchLower));
        }

        if (tagLower != null)
        {
            var videoIdsWithTag = await videoTagRepository.FindAsync(
                new SearchOptions<VideoTag>
                {
                    CancellationToken = cancellationToken,
                    Query = x => x.Tag.Name.ToLower() == tagLower,
                    Include = q => q.Include(x => x.Tag)
                },
                x => x.VideoId);

            var taggedVideoIds = videoIdsWithTag.ToHashSet();
            videoPredicate = videoPredicate.And(x => taggedVideoIds.Contains(x.Id));
        }

        var videos = await videoRepository.FindAsync(new SearchOptions<Video>
        {
            CancellationToken = cancellationToken,
            Query = videoPredicate,
            OrderBy = q => q.OrderByDescending(x => x.DownloadedAt),
            PageNumber = videoPage,
            PageSize = pageSize,
            Include = q => q.Include(x => x.Channel)
        }, x => new
        {
            x.Id,
            x.VideoId,
            x.Title,
            x.ThumbnailUrl,
            x.Duration,
            x.UploadDate,
            x.DownloadedAt,
            ChannelName = x.Channel.Name,
            ChannelId = x.Channel.Id
        });

        return Ok(new
        {
            channels = new
            {
                items = channels,
                totalCount = channels.ItemCount,
                totalPages = channels.PageCount
            },
            playlists = new
            {
                items = playlistsPage,
                totalCount = playlistsTotalCount,
                totalPages = playlistsTotalPages
            },
            videos = new
            {
                items = videos,
                totalCount = videos.ItemCount,
                totalPages = videos.PageCount
            }
        });
    }

    private sealed record PlaylistSearchResult(
        int Id,
        string? ExternalPlaylistId,
        string Name,
        string? ThumbnailUrl,
        string Platform,
        int? VideoCount,
        string? ChannelName,
        int? ChannelId,
        bool IsCustom);
}
