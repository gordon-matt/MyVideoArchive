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
    }
}