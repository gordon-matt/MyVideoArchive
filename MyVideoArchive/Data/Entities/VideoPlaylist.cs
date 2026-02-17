using System.Runtime.Serialization;
using Extenso.Data.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyVideoArchive.Data.Entities;

/// <summary>
/// Junction table for many-to-many relationship between Videos and Playlists
/// </summary>
public class VideoPlaylist : IEntity
{
    public int VideoId { get; set; }

    public int PlaylistId { get; set; }

    public Video Video { get; set; } = null!;

    public Playlist Playlist { get; set; } = null!;

    [IgnoreDataMember]
    public object[] KeyValues => [PlaylistId, VideoId];
}

public class VideoPlaylistMap : IEntityTypeConfiguration<VideoPlaylist>
{
    public void Configure(EntityTypeBuilder<VideoPlaylist> builder)
    {
        builder.ToTable("VideoPlaylists");

        // Composite primary key
        builder.HasKey(vp => new { vp.PlaylistId, vp.VideoId });

        // Relationships
        builder.HasOne(vp => vp.Video)
            .WithMany(vp => vp.VideoPlaylists)
            .HasForeignKey(vp => vp.VideoId)
            .OnDelete(DeleteBehavior.ClientNoAction);

        builder.HasOne(vp => vp.Playlist)
            .WithMany(vp => vp.VideoPlaylists)
            .HasForeignKey(vp => vp.PlaylistId)
            .OnDelete(DeleteBehavior.ClientNoAction);
    }
}
