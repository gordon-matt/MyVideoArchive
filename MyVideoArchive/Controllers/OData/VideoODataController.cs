using Extenso.AspNetCore.OData;
using Microsoft.AspNetCore.OData.Query;

namespace MyVideoArchive.Controllers.OData;

[Authorize]
public class VideoODataController : BaseODataController<Video, int>
{
    private readonly IUserContextService userContextService;
    private readonly IRepository<UserChannel> userChannelRepository;
    private readonly IRepository<Tag> tagRepository;
    private readonly IRepository<VideoTag> videoTagRepository;

    public VideoODataController(
        IAuthorizationService authorizationService,
        IUserContextService userContextService,
        IRepository<Video> repository,
        IRepository<UserChannel> userChannelRepository,
        IRepository<Tag> tagRepository,
        IRepository<VideoTag> videoTagRepository)
        : base(authorizationService, repository)
    {
        this.userContextService = userContextService;
        this.userChannelRepository = userChannelRepository;
        this.tagRepository = tagRepository;
        this.videoTagRepository = videoTagRepository;
    }

    protected override int GetId(Video entity) => entity.Id;

    protected override void SetNewId(Video entity)
    {
    }

    public override async Task<IActionResult> Get(ODataQueryOptions<Video> options, CancellationToken cancellationToken)
    {
        if (!await AuthorizeAsync(ReadPermission))
        {
            return Unauthorized();
        }

        string? userId = userContextService.GetCurrentUserId();

        var connection = GetDisposableConnection();
        var userChannelConnection = userChannelRepository.UseConnection(connection);

        var query = User.IsInRole(Constants.Roles.Administrator)
            ? connection.Query()
            : from video in connection.Query()
              join uc in userChannelConnection.Query() on video.ChannelId equals uc.ChannelId
              where uc.UserId == userId
              select video;

        var results = options.ApplyTo(query, IgnoreQueryOptions);
        return Ok(results);
    }

    protected override async Task<bool> CanModifyEntityAsync(Video entity) =>
        User.IsInRole(Constants.Roles.Administrator);

    protected override async Task<bool> CanViewEntityAsync(Video entity)
    {
        if (User.IsInRole(Constants.Roles.Administrator))
        {
            return true;
        }

        string? userId = userContextService.GetCurrentUserId();
        bool canView = await userChannelRepository.ExistsAsync(x =>
            x.UserId == userId &&
            x.ChannelId == entity.ChannelId);

        if (!canView)
        {
            // Standalone videos — check global tag first, then fall back to legacy per-user tag.
            var standaloneTagIds = await tagRepository.FindAsync(new SearchOptions<Tag>
            {
                Query = x =>
                    (x.UserId == Constants.GlobalUserId || x.UserId == userId) &&
                    x.Name == Constants.StandaloneTag
            }, x => x.Id);

            var standaloneTagIdList = standaloneTagIds.ToList();
            if (standaloneTagIdList.Count > 0)
            {
                return await videoTagRepository.ExistsAsync(x =>
                    x.VideoId == entity.Id &&
                    standaloneTagIdList.Contains(x.TagId));
            }
        }

        return canView;
    }
}