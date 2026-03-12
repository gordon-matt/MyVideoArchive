using System.Runtime.Serialization;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyVideoArchive.Data.Entities;

public class PlaylistTag : IEntity
{
    public required int PlaylistId { get; set; }

    public required int TagId { get; set; }

    public virtual Playlist Playlist { get; set; } = null!;

    public virtual Tag Tag { get; set; } = null!;

    [IgnoreDataMember]
    public object[] KeyValues => [PlaylistId, TagId];
}

public class PlaylistTagMap : IEntityTypeConfiguration<PlaylistTag>
{
    public void Configure(EntityTypeBuilder<PlaylistTag> builder)
    {
        builder.ToTable("PlaylistTags", "app");

        // Composite primary key
        builder.HasKey(m => new { m.PlaylistId, m.TagId });
        builder.Property(m => m.PlaylistId).IsRequired();
        builder.Property(m => m.TagId).IsRequired();

        // Relationships
        builder.HasOne(m => m.Playlist)
            .WithMany(v => v.PlaylistTags)
            .HasForeignKey(m => m.PlaylistId)
            .OnDelete(DeleteBehavior.ClientNoAction);

        builder.HasOne(m => m.Tag)
            .WithMany()
            .HasForeignKey(m => m.TagId)
            .OnDelete(DeleteBehavior.ClientNoAction);
    }
}