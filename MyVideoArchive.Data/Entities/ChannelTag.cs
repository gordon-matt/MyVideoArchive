using System.Runtime.Serialization;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyVideoArchive.Data.Entities;

public class ChannelTag : IEntity
{
    public required int ChannelId { get; set; }

    public required int TagId { get; set; }

    public virtual Channel Channel { get; set; } = null!;

    public virtual Tag Tag { get; set; } = null!;

    [IgnoreDataMember]
    public object[] KeyValues => [ChannelId, TagId];
}

public class ChannelTagMap : IEntityTypeConfiguration<ChannelTag>
{
    public void Configure(EntityTypeBuilder<ChannelTag> builder)
    {
        builder.ToTable("ChannelTags", "app");

        // Composite primary key
        builder.HasKey(m => new { m.ChannelId, m.TagId });
        builder.Property(m => m.ChannelId).IsRequired();
        builder.Property(m => m.TagId).IsRequired();

        // Relationships
        builder.HasOne(m => m.Channel)
            .WithMany(v => v.ChannelTags)
            .HasForeignKey(m => m.ChannelId)
            .OnDelete(DeleteBehavior.ClientNoAction);

        builder.HasOne(m => m.Tag)
            .WithMany()
            .HasForeignKey(m => m.TagId)
            .OnDelete(DeleteBehavior.ClientNoAction);
    }
}