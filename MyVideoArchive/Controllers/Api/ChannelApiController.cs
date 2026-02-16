using Extenso.AspNetCore.OData;
using Extenso.Data.Entity;
using Microsoft.AspNetCore.Authorization;
using MyVideoArchive.Data.Entities;

namespace MyVideoArchive.Controllers.Api;

public class ChannelApiController(IAuthorizationService authorizationService, IRepository<Channel> repository)
    : BaseODataController<Channel, int>(authorizationService, repository)
{
    protected override int GetId(Channel entity) => entity.Id;

    protected override void SetNewId(Channel entity)
    {
    }
}
