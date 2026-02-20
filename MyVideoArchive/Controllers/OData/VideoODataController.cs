using Extenso.AspNetCore.OData;
using Extenso.Data.Entity;
using Microsoft.AspNetCore.Authorization;
using MyVideoArchive.Data.Entities;

namespace MyVideoArchive.Controllers.OData;

public class VideoODataController(IAuthorizationService authorizationService, IRepository<Video> repository)
    : BaseODataController<Video, int>(authorizationService, repository)
{
    protected override int GetId(Video entity) => entity.Id;

    protected override void SetNewId(Video entity)
    {
    }
}
