using Extenso.AspNetCore.OData;
using Microsoft.AspNetCore.OData;
using Microsoft.OData.ModelBuilder;
using MyVideoArchive.Data.Entities;

namespace MyVideoArchive.Infrastructure;

public class ODataRegistrar : IODataRegistrar
{
    public void Register(ODataOptions options)
    {
        ODataModelBuilder builder = new ODataConventionModelBuilder();

        // Configure Channel
        var channelEntity = builder.EntitySet<Channel>("ChannelApi").EntityType;
        channelEntity.HasKey(c => c.Id);

        // Configure Video
        var videoEntity = builder.EntitySet<Video>("VideoApi").EntityType;
        videoEntity.HasKey(v => v.Id);

        // Configure Playlist
        var playlistEntity = builder.EntitySet<Playlist>("PlaylistApi").EntityType;
        playlistEntity.HasKey(p => p.Id);

        options.AddRouteComponents("odata", builder.GetEdmModel());
    }
}
