using Ardalis.Result;
using MyVideoArchive.Models.Responses;
using MyVideoArchive.Models.Video;

namespace MyVideoArchive.Services;

public class TagService : ITagService
{
    private readonly ILogger<TagService> logger;
    private readonly IUserContextService userContextService;
    private readonly IRepository<Tag> tagRepository;
    private readonly IRepository<Video> videoRepository;
    private readonly IRepository<VideoTag> videoTagRepository;

    public TagService(
        ILogger<TagService> logger,
        IUserContextService userContextService,
        IRepository<Tag> tagRepository,
        IRepository<Video> videoRepository,
        IRepository<VideoTag> videoTagRepository)
    {
        this.logger = logger;
        this.userContextService = userContextService;
        this.tagRepository = tagRepository;
        this.videoRepository = videoRepository;
        this.videoTagRepository = videoTagRepository;
    }

    public async Task<Result<GlobalTagItem>> CreateGlobalTagAsync(string name)
    {
        try
        {
            name = name.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return Result.Invalid([new ValidationError("Name", "Tag name cannot be empty")]);
            }

            name = name.ToLower();
            if (name == Constants.StandaloneTag)
            {
                return Result.Forbidden("The standalone tag is reserved as a special system tag.");
            }

            // Ensure no duplicate global tag with the same name.
            bool tagExists = await tagRepository.ExistsAsync(x =>
                x.UserId == Constants.GlobalUserId &&
                x.Name.ToLower() == name);

            if (tagExists)
            {
                return Result.Invalid([new ValidationError("Name", "A global tag with this name already exists")]);
            }

            var globalTag = await tagRepository.InsertAsync(new Tag
            {
                UserId = Constants.GlobalUserId,
                Name = name
            });

            // Consolidate any user tags with the same name into this global tag.
            var duplicateUserTags = await tagRepository.FindAsync(new SearchOptions<Tag>
            {
                Query = x =>
                    x.UserId != Constants.GlobalUserId &&
                    x.Name.ToLower() == name
            });

            var duplicateUserTagIds = duplicateUserTags.Select(t => t.Id).ToList();

            var videoTagsToUpdate = new List<VideoTag>();
            var videoTagsToDelete = new List<VideoTag>();

            var allLinkedVideoTags = await videoTagRepository.FindAsync(new SearchOptions<VideoTag>
            {
                Query = x => duplicateUserTagIds.Contains(x.TagId)
            });

            foreach (var userTag in duplicateUserTags)
            {
                // Re-point all VideoTag rows that referenced the old user tag.
                var linkedVideoTags = allLinkedVideoTags
                    .Where(x => x.TagId == userTag.Id)
                    .ToArray();

                var linkedVideoIds = linkedVideoTags.Select(x => x.VideoId).ToList();

                var videoIdsAlreadyLinkedToGlobal = await videoTagRepository.FindAsync(
                    new SearchOptions<VideoTag>
                    {
                        Query = x =>
                            x.TagId == globalTag.Id &&
                            linkedVideoIds.Contains(x.VideoId)
                    },
                    x => x.VideoId);

                foreach (var vt in linkedVideoTags)
                {
                    // Skip if VideoTag for this video already points at the global tag.
                    bool alreadyLinked = videoIdsAlreadyLinkedToGlobal.Contains(vt.VideoId);

                    if (!alreadyLinked)
                    {
                        vt.TagId = globalTag.Id;
                        videoTagsToUpdate.Add(vt);
                    }
                    else
                    {
                        videoTagsToDelete.Add(vt);
                    }
                }
            }

            await videoTagRepository.UpdateAsync(videoTagsToUpdate);
            await videoTagRepository.DeleteAsync(x => duplicateUserTagIds.Contains(x.TagId));
            await tagRepository.DeleteAsync(x => duplicateUserTagIds.Contains(x.Id));

            int usageCount = await videoTagRepository.CountAsync(x => x.TagId == globalTag.Id);
            return Result.Success(new GlobalTagItem(globalTag.Id, globalTag.Name, usageCount));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error creating global tag '{Name}'", name);
            }

            return Result.Error("An error occurred while creating the global tag");
        }
    }

    public async Task<Result> DeleteGlobalTagAsync(int tagId)
    {
        try
        {
            var tag = await tagRepository.FindOneAsync(new SearchOptions<Tag>
            {
                Query = x => x.Id == tagId && x.UserId == Constants.GlobalUserId
            });

            if (tag is null)
            {
                return Result.NotFound("Global tag not found");
            }

            await videoTagRepository.DeleteAsync(x => x.TagId == tagId);
            await tagRepository.DeleteAsync(tag);

            return Result.Success();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error deleting global tag {TagId}", tagId);
            }

            return Result.Error("An error occurred while deleting the global tag");
        }
    }

    public async Task<Result<GetGlobalTagsResponse>> GetGlobalTagsAsync()
    {
        try
        {
            var globalTags = await tagRepository.FindAsync(
                new SearchOptions<Tag>
                {
                    Query = x => x.UserId == Constants.GlobalUserId,
                    OrderBy = q => q.OrderBy(x => x.Name)
                });

            var items = new List<GlobalTagItem>();
            foreach (var tag in globalTags)
            {
                int count = await videoTagRepository.CountAsync(x => x.TagId == tag.Id);
                items.Add(new GlobalTagItem(tag.Id, tag.Name, count));
            }

            return Result.Success(new GetGlobalTagsResponse(items));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving global tags");
            }

            return Result.Error("An error occurred while retrieving global tags");
        }
    }

    public async Task<Tag> GetOrCreateTagAsync(string userId, string name)
    {
        string nameLower = name.ToLower();

        // Prefer a global tag with the same name so suggestions stay deduplicated.
        var globalTag = await tagRepository.FindOneAsync(new SearchOptions<Tag>
        {
            Query = x => x.UserId == Constants.GlobalUserId && x.Name.ToLower() == nameLower
        });

        if (globalTag is not null)
        {
            return globalTag;
        }

        var existing = await tagRepository.FindOneAsync(new SearchOptions<Tag>
        {
            Query = x => x.UserId == userId && x.Name.ToLower() == nameLower
        });

        if (existing is not null)
        {
            return existing;
        }

        var tag = new Tag { UserId = userId, Name = name };
        await tagRepository.InsertAsync(tag);
        return tag;
    }

    /// <summary>
    /// Gets or creates the current user's "standalone" tag. Used for visibility and when tagging standalone videos.
    /// Only the user's own tag is returned so that non-admin users see only videos they have marked as standalone.
    /// </summary>
    public async Task<Tag> GetStandaloneTagAsync(string userId) =>
        await tagRepository.FindOneAsync(new SearchOptions<Tag>
        {
            Query = x => x.UserId == userId && x.Name == Constants.StandaloneTag
        })
        ?? await tagRepository.InsertAsync(new Tag
        {
            UserId = userId,
            Name = Constants.StandaloneTag
        });

    public async Task<IEnumerable<int>> GetTagIdsByNameAsync(string userId, IEnumerable<string> tagNames) =>
        await tagRepository.FindAsync(new SearchOptions<Tag>
        {
            Query = x => (x.UserId == userId || x.UserId == Constants.GlobalUserId) && tagNames.Contains(x.Name)
        },
        x => x.Id);

    public async Task<Result<GetUserTagsResponse>> GetUserTagsAsync()
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            // Return user-specific tags merged with global tags, deduped by name (global takes precedence).
            var tags = await tagRepository.FindAsync(
                new SearchOptions<Tag>
                {
                    Query = x => x.UserId == userId || x.UserId == Constants.GlobalUserId,
                    OrderBy = q => q.OrderBy(x => x.Name)
                },
                x => new UserTagItem(x.Id, x.Name));

            var deduped = tags
                .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(t => t.Name)
                .ToList();

            return Result.Success(new GetUserTagsResponse(deduped));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving tags for user");
            }

            return Result.Error("An error occurred while retrieving tags");
        }
    }

    public async Task<Result<GetVideoTagsResponse>> GetVideoTagsAsync(int videoId)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            bool videoExists = await videoRepository.ExistsAsync(x => x.Id == videoId);
            if (!videoExists)
            {
                return Result.NotFound("Video not found");
            }

            // Include both this user's own tags and any global tags on the video.
            var videoTags = await videoTagRepository.FindAsync(new SearchOptions<VideoTag>
            {
                Query = x =>
                    x.VideoId == videoId &&
                    (x.Tag.UserId == userId || x.Tag.UserId == Constants.GlobalUserId),
                Include = q => q.Include(x => x.Tag)
            });

            // TODO: Should see if DistinctBy can be removed in Extenso.. assuming no longer useful.
            var tags = Enumerable.DistinctBy(
                    videoTags.Select(vt => new VideoTagItem(vt.Tag.Id, vt.Tag.Name)),
                    t => t.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Result.Success(new GetVideoTagsResponse(tags));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving tags for video {VideoId}", videoId);
            }

            return Result.Error("An error occurred while retrieving tags");
        }
    }

    public async Task<Result> RemoveStandaloneTagsForChannelAsync(string userId, int channelDbId)
    {
        var standaloneTag = await GetStandaloneTagAsync(userId);

        return standaloneTag is null ? Result.NotFound() : await RemoveTagsForChannelAsync(userId, channelDbId, standaloneTag.Id);
    }

    public async Task<Result> RemoveTagsForChannelAsync(string userId, int channelDbId, int tagId)
    {
        try
        {
            // Remove standalone VideoTag entries for those videos
            int removedCount = await videoTagRepository.DeleteAsync(x =>
                x.TagId == tagId &&
                x.Video.Channel.Id == channelDbId);

            if (logger.IsEnabled(LogLevel.Information) && removedCount > 0)
            {
                logger.LogInformation(
                    "Removed standalone tags from {Count} video(s) in channel {ChannelId} for user {UserId}",
                    removedCount, channelDbId, userId);
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(ex, "Failed to remove standalone tags for channel {ChannelId} user {UserId}",
                    channelDbId, userId);
            }

            return Result.Error();
        }
    }

    public async Task<Result> SetVideoTagsAsync(int videoId, SetVideoTagsRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            bool videoExists = await videoRepository.ExistsAsync(x => x.Id == videoId);
            if (!videoExists)
            {
                return Result.NotFound("Video not found");
            }

            var selectedTags = request.TagNames
                ?.Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];

            var standaloneTag = await GetStandaloneTagAsync(userId);

            // Existing tags on this video that belong to the current user OR are global.
            // We never delete global-tag VideoTag rows here — the user can't remove a global tag.
            var existingTags = await videoTagRepository.FindAsync(new SearchOptions<VideoTag>
            {
                Query = x =>
                    x.TagId != standaloneTag.Id &&
                    x.VideoId == videoId &&
                    (x.Tag.UserId == userId || x.Tag.UserId == Constants.GlobalUserId),

                Include = query => query.Include(x => x.Tag)
            });

            var toAdd = selectedTags
                .Where(t => !existingTags.Any(td => td.Tag.Name.Equals(t, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            // Only delete user-owned tags (never touch global ones).
            var toDelete = existingTags
                .Where(td =>
                    td.Tag.UserId != Constants.GlobalUserId &&
                    !selectedTags.Any(t => t.Equals(td.Tag.Name, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (!toDelete.IsNullOrEmpty())
            {
                await videoTagRepository.DeleteAsync(toDelete);
            }

            if (!toAdd.IsNullOrEmpty())
            {
                foreach (string tagName in toAdd)
                {
                    var tag = await GetOrCreateTagAsync(userId, tagName);
                    await videoTagRepository.InsertAsync(new VideoTag
                    {
                        VideoId = videoId,
                        TagId = tag.Id
                    });
                }
            }

            // Garbage-collect orphaned user tags (never touch global or standalone tags).
            using var videoTagConnection = videoTagRepository.OpenConnection();
            using var tagConnection = tagRepository.UseConnection(videoTagConnection);

            var unusedTagIdsQuery = from tag in tagConnection.Query()
                                    join videoTag in videoTagConnection.Query()
                                        on tag.Id equals videoTag.TagId into videoTags
                                    where tag.UserId == userId
                                       && tag.Id != standaloneTag.Id
                                       && !videoTags.Any()
                                    select tag.Id;

            var unusedTagIds = await unusedTagIdsQuery.ToListAsync();
            if (!unusedTagIds.IsNullOrEmpty())
            {
                await tagRepository.DeleteAsync(x => unusedTagIds.Contains(x.Id));
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error setting tags for video {VideoId}", videoId);
            }

            return Result.Error("An error occurred while updating tags");
        }
    }
}