using Extenso.AspNetCore.OData;
using Microsoft.AspNetCore.OData;
using Microsoft.OData.ModelBuilder;

namespace MyVideoArchive.Infrastructure;

public class ODataRegistrar : IODataRegistrar
{
    public void Register(ODataOptions options)
    {
        ODataModelBuilder builder = new ODataConventionModelBuilder();

        // Configure Channel
        var channelEntity = builder.EntitySet<Channel>("ChannelOData").EntityType;
        channelEntity.HasKey(x => x.Id);

        // Configure Video
        var videoEntity = builder.EntitySet<Video>("VideoOData").EntityType;
        videoEntity.HasKey(x => x.Id);

        // Configure Playlist
        var playlistEntity = builder.EntitySet<Playlist>("PlaylistOData").EntityType;
        playlistEntity.HasKey(x => x.Id);

        options.AddRouteComponents("odata", builder.GetEdmModel());
    }
}