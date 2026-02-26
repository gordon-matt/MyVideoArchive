using Extenso.AspNetCore.OData;
using Microsoft.AspNetCore.OData.Query;

namespace MyVideoArchive.Controllers.OData;

[Authorize]
public class VideoODataController : BaseODataController<Video, int>
{
    private readonly IUserContextService userContextService;
    private readonly IRepository<UserChannel> userChannelRepository;

    public VideoODataController(
        IAuthorizationService authorizationService,
        IUserContextService userContextService,
        IRepository<Video> repository,
        IRepository<UserChannel> userChannelRepository)
        : base(authorizationService, repository)
    {
        this.userContextService = userContextService;
        this.userChannelRepository = userChannelRepository;
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

        var userId = userContextService.GetCurrentUserId();

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

        var userId = userContextService.GetCurrentUserId();
        return await userChannelRepository.ExistsAsync(x => x.UserId == userId && x.ChannelId == entity.ChannelId);
    }
}