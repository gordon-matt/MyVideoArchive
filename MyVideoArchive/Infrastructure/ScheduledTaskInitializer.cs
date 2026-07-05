using Hangfire;

namespace MyVideoArchive.Infrastructure;

public static class ScheduledTaskInitializer
{
    public static void Initialize()
    {
        // Schedule recurring jobs
        RecurringJob.AddOrUpdate<ChannelSyncJob>(
            "sync-all-channels",
            job => job.SyncAllChannelsAsync(CancellationToken.None),
            Cron.Weekly()); // Check for new videos every day

        RecurringJob.AddOrUpdate<PlaylistSyncJob>(
            "sync-all-playlists",
            job => job.SyncAllPlaylistsAsync(CancellationToken.None),
            Cron.Weekly()); // Check for new playlist videos every day

        RecurringJob.AddOrUpdate<TagGarbageCollectorJob>(
            "tag-garbage-collector",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Daily()); // Clean up unused per-user tags once per day

        RecurringJob.AddOrUpdate<MetadataReviewJob>(
            "metadata-review",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Weekly()); // Retry previously unavailable video metadata once per week

        // Fetch missing sidecar subtitle files for already-downloaded videos.
        // No-ops when Subtitles:Enabled is false; can also be triggered
        // manually from the Hangfire dashboard.
        RecurringJob.AddOrUpdate<SubtitleBackfillJob>(
            "subtitle-backfill",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Weekly());

        // Keep yt-dlp current so YouTube API changes don't break fetches.
        // No-ops when YoutubeDL:AutoUpdate:Enabled is false; can also be
        // triggered manually from the Hangfire dashboard.
        RecurringJob.AddOrUpdate<YtDlpUpdateJob>(
            "yt-dlp-update",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Weekly());
    }
}