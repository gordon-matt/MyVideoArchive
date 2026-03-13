using System.Runtime.Serialization;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyVideoArchive.Data.Entities;

public class CustomPlaylistTag : IEntity
{
    public required int CustomPlaylistId { get; set; }

    public required int TagId { get; set; }

    public virtual CustomPlaylist CustomPlaylist { get; set; } = null!;

    public virtual Tag Tag { get; set; } = null!;

    [IgnoreDataMember]
    public object[] KeyValues => [CustomPlaylistId, TagId];
}

public class CustomPlaylistTagMap : IEntityTypeConfiguration<CustomPlaylistTag>
{
    public void Configure(EntityTypeBuilder<CustomPlaylistTag> builder)
    {
        builder.ToTable("CustomPlaylistTags", "app");

        builder.HasKey(m => new { m.CustomPlaylistId, m.TagId });
        builder.Property(m => m.CustomPlaylistId).IsRequired();
        builder.Property(m => m.TagId).IsRequired();

        builder.HasOne(m => m.CustomPlaylist)
            .WithMany()
            .HasForeignKey(m => m.CustomPlaylistId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Tag)
            .WithMany()
            .HasForeignKey(m => m.TagId)
            .OnDelete(DeleteBehavior.ClientNoAction);
    }
}
