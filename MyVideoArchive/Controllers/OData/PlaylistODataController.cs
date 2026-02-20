using Extenso.AspNetCore.OData;
using Extenso.Data.Entity;
using Microsoft.AspNetCore.Authorization;
using MyVideoArchive.Data.Entities;

namespace MyVideoArchive.Controllers.OData;

public class PlaylistODataController(IAuthorizationService authorizationService, IRepository<Playlist> repository)
    : BaseODataController<Playlist, int>(authorizationService, repository)
{
    protected override int GetId(Playlist entity) => entity.Id;

    protected override void SetNewId(Playlist entity)
    {
    }
}
