using Extenso.AspNetCore.OData;

namespace MyVideoArchive.Controllers.OData;

[Authorize]
public class PlaylistODataController(IAuthorizationService authorizationService, IRepository<Playlist> repository)
    : BaseODataController<Playlist, int>(authorizationService, repository)
{
    protected override int GetId(Playlist entity) => entity.Id;

    protected override void SetNewId(Playlist entity)
    {
    }
}