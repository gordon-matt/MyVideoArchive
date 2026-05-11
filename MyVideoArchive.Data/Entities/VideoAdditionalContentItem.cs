using System.Runtime.Serialization;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyVideoArchive.Data.Entities;

/// <summary>
/// Junction: an additional-content file may be associated with zero or many videos.
/// </summary>
public class VideoAdditionalContentItem : IEntity
{
    public int VideoId { get; set; }

    public int AdditionalContentItemId { get; set; }

    public virtual Video Video { get; set; } = null!;

    public virtual AdditionalContentItem AdditionalContentItem { get; set; } = null!;

    [IgnoreDataMember]
    public object[] KeyValues => [VideoId, AdditionalContentItemId];
}

public class VideoAdditionalContentItemMap : IEntityTypeConfiguration<VideoAdditionalContentItem>
{
    public void Configure(EntityTypeBuilder<VideoAdditionalContentItem> builder)
    {
        builder.ToTable("VideoAdditionalContent", "app");
        builder.HasKey(x => new { x.VideoId, x.AdditionalContentItemId });

        builder.HasOne(x => x.Video)
            .WithMany(v => v.VideoAdditionalContentItems)
            .HasForeignKey(x => x.VideoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.AdditionalContentItem)
            .WithMany(a => a.VideoLinks)
            .HasForeignKey(x => x.AdditionalContentItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
