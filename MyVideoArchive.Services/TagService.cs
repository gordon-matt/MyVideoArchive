using Ardalis.Result;
using MyVideoArchive.Data.Entities;
using MyVideoArchive.Models.Requests.Channel;
using MyVideoArchive.Models.Requests.Playlist;
using MyVideoArchive.Models.Responses;
using MyVideoArchive.Models.Video;

namespace MyVideoArchive.Services;

public class TagService : ITagService
{
    private readonly IRepository<ChannelTag> channelTagRepository;
    private readonly IRepository<CustomPlaylist> customPlaylistRepository;
    private readonly IRepository<CustomPlaylistTag> customPlaylistTagRepository;
    private readonly ILogger<TagService> logger;
    private readonly IRepository<PlaylistTag> playlistTagRepository;
    private readonly IRepository<Tag> tagRepository;
    private readonly IUserContextService userContextService;
    private readonly IRepository<Video> videoRepository;
    private readonly IRepository<VideoTag> videoTagRepository;

    public TagService(
        ILogger<TagService> logger,
        IUserContextService userContextService,
        IRepository<Tag> tagRepository,
        IRepository<Video> videoRepository,
        IRepository<VideoTag> videoTagRepository,
        IRepository<ChannelTag> channelTagRepository,
        IRepository<PlaylistTag> playlistTagRepository,
        IRepository<CustomPlaylistTag> customPlaylistTagRepository,
        IRepository<CustomPlaylist> customPlaylistRepository)
    {
        this.logger = logger;
        this.userContextService = userContextService;
        this.tagRepository = tagRepository;
        this.videoRepository = videoRepository;
        this.videoTagRepository = videoTagRepository;
        this.channelTagRepository = channelTagRepository;
        this.playlistTagRepository = playlistTagRepository;
        this.customPlaylistTagRepository = customPlaylistTagRepository;
        this.customPlaylistRepository = customPlaylistRepository;
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

            await channelTagRepository.DeleteAsync(x => x.TagId == tagId);
            await playlistTagRepository.DeleteAsync(x => x.TagId == tagId);
            await customPlaylistTagRepository.DeleteAsync(x => x.TagId == tagId);
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

    public async Task<Result<GetChannelTagsResponse>> GetChannelTagsAsync(int channelId)
    {
        try
        {
            var channelTags = await channelTagRepository.FindAsync(new SearchOptions<ChannelTag>
            {
                Query = x => x.ChannelId == channelId,
                Include = q => q.Include(x => x.Tag)
            });

            var items = channelTags
                .Select(ct => new ChannelTagItem(ct.Tag.Id, ct.Tag.Name))
                .OrderBy(t => t.Name)
                .ToList();

            return Result.Success(new GetChannelTagsResponse(items));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving tags for channel {ChannelId}", channelId);
            }

            return Result.Error("An error occurred while retrieving channel tags");
        }
    }

    public async Task<Result<GetPlaylistTagsResponse>> GetCustomPlaylistTagsAsync(int customPlaylistId)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            var playlist = await customPlaylistRepository.FindOneAsync(new SearchOptions<CustomPlaylist>
            {
                Query = x => x.Id == customPlaylistId && x.UserId == userId
            });
            if (playlist is null)
            {
                return Result.NotFound("Playlist not found");
            }

            var customPlaylistTags = await customPlaylistTagRepository.FindAsync(new SearchOptions<CustomPlaylistTag>
            {
                Query = x => x.CustomPlaylistId == customPlaylistId,
                Include = q => q.Include(x => x.Tag)
            });

            var items = customPlaylistTags
                .Select(cpt => new PlaylistTagItem(cpt.Tag.Id, cpt.Tag.Name))
                .OrderBy(t => t.Name)
                .ToList();

            return Result.Success(new GetPlaylistTagsResponse(items));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving tags for custom playlist {CustomPlaylistId}", customPlaylistId);
            }

            return Result.Error("An error occurred while retrieving custom playlist tags");
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

    public async Task<Tag> GetOrCreateGlobalTagAsync(string name)
    {
        name = name.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Tag name cannot be empty", nameof(name));
        }

        var existing = await tagRepository.FindOneAsync(new SearchOptions<Tag>
        {
            Query = x => x.UserId == Constants.GlobalUserId && x.Name.ToLower() == name
        });

        if (existing is not null)
        {
            return existing;
        }

        return await tagRepository.InsertAsync(new Tag { UserId = Constants.GlobalUserId, Name = name });
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

    public async Task<Result<GetPlaylistTagsResponse>> GetPlaylistTagsAsync(int playlistId)
    {
        try
        {
            var playlistTags = await playlistTagRepository.FindAsync(new SearchOptions<PlaylistTag>
            {
                Query = x => x.PlaylistId == playlistId,
                Include = q => q.Include(x => x.Tag)
            });

            var items = playlistTags
                .Select(pt => new PlaylistTagItem(pt.Tag.Id, pt.Tag.Name))
                .OrderBy(t => t.Name)
                .ToList();

            return Result.Success(new GetPlaylistTagsResponse(items));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving tags for playlist {PlaylistId}", playlistId);
            }

            return Result.Error("An error occurred while retrieving playlist tags");
        }
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
            // Exclude the standalone tag — it is a system tag managed automatically and
            // must not be shown or editable in the UI.
            var videoTags = await videoTagRepository.FindAsync(new SearchOptions<VideoTag>
            {
                Query = x =>
                    x.VideoId == videoId &&
                    x.Tag.Name != Constants.StandaloneTag &&
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

    public async Task ImportChannelTagsAsync(int channelId, IEnumerable<string> tagNames)
    {
        try
        {
            var names = tagNames
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (names.Count == 0)
            {
                return;
            }

            // Only import once — skip if any tags are already set on this channel.
            bool hasExistingTags = await channelTagRepository.ExistsAsync(x => x.ChannelId == channelId);
            if (hasExistingTags)
            {
                return;
            }

            foreach (string name in names)
            {
                var tag = await GetOrCreateGlobalTagAsync(name);
                await channelTagRepository.InsertAsync(new ChannelTag { ChannelId = channelId, TagId = tag.Id });
            }
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(ex, "Failed to import tags for channel {ChannelId}", channelId);
            }
        }
    }

    public async Task ImportPlaylistTagsAsync(int playlistId, IEnumerable<string> tagNames)
    {
        try
        {
            var names = tagNames
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (names.Count == 0)
            {
                return;
            }

            // Only import once — skip if any tags are already set on this playlist.
            bool hasExistingTags = await playlistTagRepository.ExistsAsync(x => x.PlaylistId == playlistId);
            if (hasExistingTags)
            {
                return;
            }

            foreach (string name in names)
            {
                var tag = await GetOrCreateGlobalTagAsync(name);
                await playlistTagRepository.InsertAsync(new PlaylistTag { PlaylistId = playlistId, TagId = tag.Id });
            }
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(ex, "Failed to import tags for playlist {PlaylistId}", playlistId);
            }
        }
    }

    public async Task ImportVideoTagsAsync(int videoId, IEnumerable<string> tagNames)
    {
        try
        {
            var names = tagNames
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (names.Count == 0)
            {
                return;
            }

            // Only import once — skip if any non-standalone tags are already set on this video.
            bool hasExistingTags = await videoTagRepository.ExistsAsync(x =>
                x.VideoId == videoId &&
                x.Tag.Name != Constants.StandaloneTag);
            if (hasExistingTags)
            {
                return;
            }

            foreach (string name in names)
            {
                var tag = await GetOrCreateGlobalTagAsync(name);
                await videoTagRepository.InsertAsync(new VideoTag { VideoId = videoId, TagId = tag.Id });
            }
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(ex, "Failed to import tags for video {VideoId}", videoId);
            }
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
                    "Removed tags from {Count} video(s) in channel {ChannelId} for user {UserId}",
                    removedCount, channelDbId, userId);
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(ex, "Failed to remove tags for channel {ChannelId} user {UserId}",
                    channelDbId, userId);
            }

            return Result.Error();
        }
    }

    public async Task<Result> SetChannelTagsAsync(int channelId, SetChannelTagsRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            var selectedTagNames = request.TagNames
                ?.Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];

            var existingChannelTags = await channelTagRepository.FindAsync(new SearchOptions<ChannelTag>
            {
                Query = x => x.ChannelId == channelId,
                Include = q => q.Include(x => x.Tag)
            });

            var existingTagNames = existingChannelTags.Select(ct => ct.Tag.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var toAdd = selectedTagNames.Where(n => !existingTagNames.Contains(n)).ToList();
            var toDelete = existingChannelTags
                .Where(ct => !selectedTagNames.Contains(ct.Tag.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (toDelete.Count > 0)
            {
                await channelTagRepository.DeleteAsync(toDelete);
            }

            foreach (string tagName in toAdd)
            {
                var tag = await GetOrCreateTagAsync(userId, tagName);
                await channelTagRepository.InsertAsync(new ChannelTag { ChannelId = channelId, TagId = tag.Id });
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error setting tags for channel {ChannelId}", channelId);
            }

            return Result.Error("An error occurred while updating channel tags");
        }
    }

    public async Task<Result> SetCustomPlaylistTagsAsync(int customPlaylistId, SetPlaylistTagsRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            var playlist = await customPlaylistRepository.FindOneAsync(new SearchOptions<CustomPlaylist>
            {
                Query = x => x.Id == customPlaylistId && x.UserId == userId
            });
            if (playlist is null)
            {
                return Result.NotFound("Playlist not found");
            }

            var selectedTagNames = request.TagNames
                ?.Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];

            var existingTags = await customPlaylistTagRepository.FindAsync(new SearchOptions<CustomPlaylistTag>
            {
                Query = x => x.CustomPlaylistId == customPlaylistId,
                Include = q => q.Include(x => x.Tag)
            });

            var existingTagNames = existingTags.Select(cpt => cpt.Tag.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var toAdd = selectedTagNames.Where(n => !existingTagNames.Contains(n)).ToList();
            var toDelete = existingTags
                .Where(cpt => !selectedTagNames.Contains(cpt.Tag.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (toDelete.Count > 0)
            {
                await customPlaylistTagRepository.DeleteAsync(toDelete);
            }

            foreach (string tagName in toAdd)
            {
                var tag = await GetOrCreateTagAsync(userId, tagName);
                await customPlaylistTagRepository.InsertAsync(new CustomPlaylistTag { CustomPlaylistId = customPlaylistId, TagId = tag.Id });
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error setting tags for custom playlist {CustomPlaylistId}", customPlaylistId);
            }

            return Result.Error("An error occurred while updating custom playlist tags");
        }
    }

    public async Task<Result> SetPlaylistTagsAsync(int playlistId, SetPlaylistTagsRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            var selectedTagNames = request.TagNames
                ?.Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];

            var existingPlaylistTags = await playlistTagRepository.FindAsync(new SearchOptions<PlaylistTag>
            {
                Query = x => x.PlaylistId == playlistId,
                Include = q => q.Include(x => x.Tag)
            });

            var existingTagNames = existingPlaylistTags.Select(pt => pt.Tag.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var toAdd = selectedTagNames.Where(n => !existingTagNames.Contains(n)).ToList();
            var toDelete = existingPlaylistTags
                .Where(pt => !selectedTagNames.Contains(pt.Tag.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (toDelete.Count > 0)
            {
                await playlistTagRepository.DeleteAsync(toDelete);
            }

            foreach (string tagName in toAdd)
            {
                var tag = await GetOrCreateTagAsync(userId, tagName);
                await playlistTagRepository.InsertAsync(new PlaylistTag { PlaylistId = playlistId, TagId = tag.Id });
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error setting tags for playlist {PlaylistId}", playlistId);
            }

            return Result.Error("An error occurred while updating playlist tags");
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

    /// <summary>
    /// Garbage-collects unused per-user tags (never touches global or standalone tags).
    /// A tag is considered unused if it is not referenced by any VideoTag, ChannelTag,
    /// PlaylistTag or CustomPlaylistTag.
    /// </summary>
    public async Task GarbageCollectUserTagsAsync()
    {
        try
        {
            var userTags = await tagRepository.FindAsync(
                new SearchOptions<Tag>
                {
                    Query = x =>
                        x.UserId != Constants.GlobalUserId &&
                        x.Name != Constants.StandaloneTag
                },
                x => new { x.Id, x.UserId, x.Name });

            if (userTags.Count == 0)
            {
                return;
            }

            var candidateIds = userTags.Select(t => t.Id).ToList();

            var stillUsedVideoTagIds = await videoTagRepository.FindAsync(
                new SearchOptions<VideoTag>
                {
                    Query = x => candidateIds.Contains(x.TagId)
                },
                x => x.TagId);

            var stillUsedChannelTagIds = await channelTagRepository.FindAsync(
                new SearchOptions<ChannelTag>
                {
                    Query = x => candidateIds.Contains(x.TagId)
                },
                x => x.TagId);

            var stillUsedPlaylistTagIds = await playlistTagRepository.FindAsync(
                new SearchOptions<PlaylistTag>
                {
                    Query = x => candidateIds.Contains(x.TagId)
                },
                x => x.TagId);

            var stillUsedCustomPlaylistTagIds = await customPlaylistTagRepository.FindAsync(
                new SearchOptions<CustomPlaylistTag>
                {
                    Query = x => candidateIds.Contains(x.TagId)
                },
                x => x.TagId);

            var usedIds = stillUsedVideoTagIds
                .Concat(stillUsedChannelTagIds)
                .Concat(stillUsedPlaylistTagIds)
                .Concat(stillUsedCustomPlaylistTagIds)
                .ToHashSet();

            var unusedIds = candidateIds
                .Where(id => !usedIds.Contains(id))
                .ToList();

            if (unusedIds.Count == 0)
            {
                return;
            }

            await tagRepository.DeleteAsync(x => unusedIds.Contains(x.Id));

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Garbage-collected {Count} unused user tag(s)", unusedIds.Count);
            }
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(ex, "Error while garbage-collecting user tags");
            }
        }
    }
}