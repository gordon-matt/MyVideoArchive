using Ardalis.Result;
using MyVideoArchive.Data;
using MyVideoArchive.Models.Responses;

namespace MyVideoArchive.Services;

public class AdminDashboardService : IAdminDashboardService
{
    private readonly ILogger<AdminDashboardService> logger;
    private readonly IDbContextFactory dbContextFactory;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<Video> videoRepository;
    private readonly IRepository<Playlist> playlistRepository;
    private readonly IRepository<CustomPlaylist> customPlaylistRepository;
    private readonly IRepository<Series> seriesRepository;
    private readonly IRepository<Tag> tagRepository;

    public AdminDashboardService(
        ILogger<AdminDashboardService> logger,
        IDbContextFactory dbContextFactory,
        IRepository<Channel> channelRepository,
        IRepository<Video> videoRepository,
        IRepository<Playlist> playlistRepository,
        IRepository<CustomPlaylist> customPlaylistRepository,
        IRepository<Series> seriesRepository,
        IRepository<Tag> tagRepository)
    {
        this.logger = logger;
        this.dbContextFactory = dbContextFactory;
        this.channelRepository = channelRepository;
        this.videoRepository = videoRepository;
        this.playlistRepository = playlistRepository;
        this.customPlaylistRepository = customPlaylistRepository;
        this.seriesRepository = seriesRepository;
        this.tagRepository = tagRepository;
    }

    public async Task<Result<AdminDashboardStatsResponse>> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = (ApplicationDbContextBase)dbContextFactory.GetContext();
            int totalUsers = await context.Users.CountAsync(cancellationToken);

            var channels = await channelRepository.FindAsync(
                new SearchOptions<Channel> { CancellationToken = cancellationToken },
                x => new { x.Id, x.Name, x.Platform, x.AvatarUrl, x.BannerUrl, x.SubscribedAt });

            var videoFacts = await videoRepository.FindAsync(
                new SearchOptions<Video> { CancellationToken = cancellationToken },
                x => new
                {
                    x.ChannelId,
                    x.Platform,
                    x.FileSize,
                    x.Duration,
                    x.DownloadedAt,
                    x.IsQueued,
                    x.DownloadFailed,
                    x.NeedsMetadataReview
                });

            int totalVideosAvailable = videoFacts.Count;
            var downloaded = videoFacts.Where(x => x.DownloadedAt != null).ToList();
            int totalVideosDownloaded = downloaded.Count;
            long totalStorageBytes = downloaded.Sum(x => x.FileSize ?? 0);
            var totalDuration = downloaded.Aggregate(TimeSpan.Zero, (acc, x) => acc + (x.Duration ?? TimeSpan.Zero));
            double totalDurationHours = totalDuration.TotalHours;

            int failedDownloadsCount = videoFacts.Count(x => x.DownloadFailed);
            int needsMetadataReviewCount = videoFacts.Count(x => x.NeedsMetadataReview);
            int queuedDownloadsCount = videoFacts.Count(x => x.IsQueued);

            var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);
            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
            int videosDownloadedLast7Days = downloaded.Count(x => x.DownloadedAt >= sevenDaysAgo);
            int videosDownloadedLast30Days = downloaded.Count(x => x.DownloadedAt >= thirtyDaysAgo);

            int totalPlaylists = await playlistRepository.CountAsync(x => true);
            int totalCustomPlaylists = await customPlaylistRepository.CountAsync(x => true);
            int totalSeries = await seriesRepository.CountAsync(x => true);
            int totalGlobalTags = await tagRepository.CountAsync(x => x.UserId == Constants.GlobalUserId);

            var videoCountByChannel = downloaded
                .GroupBy(x => x.ChannelId)
                .ToDictionary(g => g.Key, g => g.Count());

            var channelCountByPlatform = channels
                .GroupBy(x => x.Platform)
                .ToDictionary(g => g.Key, g => g.Count());

            var videoStatsByPlatform = downloaded
                .GroupBy(x => x.Platform)
                .ToDictionary(g => g.Key, g => (Count: g.Count(), Storage: g.Sum(v => v.FileSize ?? 0)));

            var channelsByPlatform = channelCountByPlatform.Keys
                .Union(videoStatsByPlatform.Keys)
                .OrderBy(p => p)
                .Select(platform => new ChannelPlatformBreakdown(
                    platform,
                    channelCountByPlatform.GetValueOrDefault(platform),
                    videoStatsByPlatform.TryGetValue(platform, out var stats) ? stats.Count : 0,
                    videoStatsByPlatform.TryGetValue(platform, out var stats2) ? stats2.Storage : 0))
                .ToList();

            var topChannels = channels
                .Select(c => new TopChannelItem(
                    c.Id,
                    c.Name,
                    c.AvatarUrl,
                    c.BannerUrl,
                    c.Platform,
                    videoCountByChannel.GetValueOrDefault(c.Id)))
                .Where(c => c.VideoCount > 0)
                .OrderByDescending(c => c.VideoCount)
                .Take(5)
                .ToList();

            var recentChannels = channels
                .OrderByDescending(c => c.SubscribedAt)
                .Take(5)
                .Select(c => new RecentChannelItem(c.Id, c.Name, c.AvatarUrl, c.Platform, c.SubscribedAt))
                .ToList();

            return Result.Success(new AdminDashboardStatsResponse(
                totalUsers,
                channels.Count,
                totalVideosDownloaded,
                totalVideosAvailable,
                totalStorageBytes,
                totalDurationHours,
                totalPlaylists,
                totalCustomPlaylists,
                totalSeries,
                totalGlobalTags,
                failedDownloadsCount,
                needsMetadataReviewCount,
                queuedDownloadsCount,
                videosDownloadedLast7Days,
                videosDownloadedLast30Days,
                channelsByPlatform,
                topChannels,
                recentChannels));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving admin dashboard stats");
            }
            return Result.Error("An error occurred while retrieving dashboard statistics");
        }
    }
}
