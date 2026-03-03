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

    public async Task<Tag> GetOrCreateTagAsync(string userId, string name)
    {
        var existing = await tagRepository.FindOneAsync(new SearchOptions<Tag>
        {
            Query = x =>
                x.UserId == userId &&
                x.Name.ToLower() == name.ToLower()
        });

        if (existing is not null)
        {
            return existing;
        }

        var tag = new Tag { UserId = userId, Name = name };
        await tagRepository.InsertAsync(tag);
        return tag;
    }

    public async Task<Tag> GetStandaloneTagAsync(string userId) =>
        await tagRepository.FindOneAsync(new SearchOptions<Tag>
        {
            Query = x =>
                x.UserId == userId &&

            x.Name == Constants.StandaloneTag
        });

    public async Task<IEnumerable<int>> GetTagIdsByNameAsync(string userId, IEnumerable<string> tagNames) =>
        await tagRepository.FindAsync(new SearchOptions<Tag>
        {
            Query = x => x.UserId == userId && tagNames.Contains(x.Name)
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

            var tags = await tagRepository.FindAsync(
                new SearchOptions<Tag>
                {
                    Query = x => x.UserId == userId,
                    OrderBy = q => q.OrderBy(x => x.Name)
                },
                x => new UserTagItem(x.Id, x.Name));

            return Result.Success(new GetUserTagsResponse(tags.ToList()));
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

            var videoTags = await videoTagRepository.FindAsync(new SearchOptions<VideoTag>
            {
                Query = x => x.VideoId == videoId && x.Tag.UserId == userId,
                Include = q => q.Include(x => x.Tag)
            });

            var tags = videoTags
                .Select(vt => new VideoTagItem(vt.Tag.Id, vt.Tag.Name))
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

            var standaloneTag = await GetOrCreateTagAsync(userId, Constants.StandaloneTag);

            var existingTags = await videoTagRepository.FindAsync(new SearchOptions<VideoTag>
            {
                Query = x =>
                    x.TagId != standaloneTag.Id &&
                    x.VideoId == videoId &&
                    x.Tag.UserId == userId,

                Include = query => query.Include(x => x.Tag)
            });

            var toAdd = selectedTags
                .Where(t => !existingTags.Any(td => td.Tag.Name.Equals(t, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var toDelete = existingTags
                .Where(td => !selectedTags.Any(t => t.Equals(td.Tag.Name, StringComparison.OrdinalIgnoreCase)))
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

            using var videoTagConnection = videoTagRepository.OpenConnection();
            using var tagConnection = tagRepository.UseConnection(videoTagConnection);

            var deletedTagIds = toDelete.Select(td => td.TagId).ToList();

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