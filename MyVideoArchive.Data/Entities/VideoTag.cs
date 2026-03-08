using System.Runtime.Serialization;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyVideoArchive.Data.Entities;

public class VideoTag : IEntity
{
    public required int VideoId { get; set; }

    public required int TagId { get; set; }

    public virtual Video Video { get; set; } = null!;

    public virtual Tag Tag { get; set; } = null!;

    [IgnoreDataMember]
    public object[] KeyValues => [VideoId, TagId];
}

public class VideoTagMap : IEntityTypeConfiguration<VideoTag>
{
    public void Configure(EntityTypeBuilder<VideoTag> builder)
    {
        builder.ToTable("VideoTags", "app");

        // Composite primary key
        builder.HasKey(m => new { m.VideoId, m.TagId });
        builder.Property(m => m.VideoId).IsRequired();
        builder.Property(m => m.TagId).IsRequired();

        // Relationships
        builder.HasOne(m => m.Video)
            .WithMany(v => v.VideoTags)
            .HasForeignKey(m => m.VideoId)
            .OnDelete(DeleteBehavior.ClientNoAction);

        builder.HasOne(m => m.Tag)
            .WithMany()
            .HasForeignKey(m => m.TagId)
            .OnDelete(DeleteBehavior.ClientNoAction);
    }
}