using Extenso.AspNetCore.OData;

namespace MyVideoArchive.Controllers.OData;

[Authorize]
public class VideoODataController(IAuthorizationService authorizationService, IRepository<Video> repository)
    : BaseODataController<Video, int>(authorizationService, repository)
{
    protected override int GetId(Video entity) => entity.Id;

    protected override void SetNewId(Video entity)
    {
    }
}