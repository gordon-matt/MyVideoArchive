using System.Runtime.Serialization;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyVideoArchive.Data.Entities;

public class SeriesPlaylist : IEntity
{
    public int SeriesId { get; set; }

    public int PlaylistId { get; set; }

    public int SortOrder { get; set; }

    public virtual Series Series { get; set; } = null!;

    public virtual Playlist Playlist { get; set; } = null!;

    [IgnoreDataMember]
    public object[] KeyValues => [SeriesId, PlaylistId];
}

public class SeriesPlaylistMap : IEntityTypeConfiguration<SeriesPlaylist>
{
    public void Configure(EntityTypeBuilder<SeriesPlaylist> builder)
    {
        builder.ToTable("SeriesPlaylists", "app");
        builder.HasKey(m => new { m.SeriesId, m.PlaylistId });

        builder.HasOne(m => m.Series)
            .WithMany(m => m.SeriesPlaylists)
            .HasForeignKey(m => m.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Playlist)
            .WithMany()
            .HasForeignKey(m => m.PlaylistId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}