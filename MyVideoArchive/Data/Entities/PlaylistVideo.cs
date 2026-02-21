using System.Runtime.Serialization;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyVideoArchive.Data.Entities;

/// <summary>
/// Junction table for many-to-many relationship between Videos and Playlists
/// </summary>
public class PlaylistVideo : IEntity
{
    public int PlaylistId { get; set; }

    public int VideoId { get; set; }

    /// <summary>
    /// Original order of the video in the playlist (from YouTube)
    /// </summary>
    public int Order { get; set; }

    public Playlist Playlist { get; set; } = null!;

    public Video Video { get; set; } = null!;

    [IgnoreDataMember]
    public object[] KeyValues => [PlaylistId, VideoId];
}

public class PlaylistVideoMap : IEntityTypeConfiguration<PlaylistVideo>
{
    public void Configure(EntityTypeBuilder<PlaylistVideo> builder)
    {
        builder.ToTable("PlaylistVideos");

        // Composite primary key
        builder.HasKey(vp => new { vp.PlaylistId, vp.VideoId });

        // Relationships
        builder.HasOne(vp => vp.Playlist)
            .WithMany(vp => vp.PlaylistVideos)
            .HasForeignKey(vp => vp.PlaylistId)
            .OnDelete(DeleteBehavior.ClientNoAction);

        builder.HasOne(vp => vp.Video)
            .WithMany(vp => vp.PlaylistVideos)
            .HasForeignKey(vp => vp.VideoId)
            .OnDelete(DeleteBehavior.ClientNoAction);
    }
}