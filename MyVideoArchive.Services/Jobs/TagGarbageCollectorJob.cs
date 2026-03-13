using Hangfire;

namespace MyVideoArchive.Services.Jobs;

/// <summary>
/// Hangfire job that runs daily to garbage-collect unused per-user tags.
/// </summary>
public class TagGarbageCollectorJob
{
    private readonly ILogger<TagGarbageCollectorJob> logger;
    private readonly ITagService tagService;

    public TagGarbageCollectorJob(ILogger<TagGarbageCollectorJob> logger, ITagService tagService)
    {
        this.logger = logger;
        this.tagService = tagService;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Starting tag garbage-collection job");
            }

            await tagService.GarbageCollectUserTagsAsync();

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Completed tag garbage-collection job");
            }
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error running tag garbage-collection job");
            }
        }
    }
}

