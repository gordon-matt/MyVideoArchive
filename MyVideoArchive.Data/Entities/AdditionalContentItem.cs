using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyVideoArchive.Data.Entities;

/// <summary>
/// A supplementary file stored under the channel's "_extras" folder. Playlist and video
/// scoping is expressed via <see cref="PlaylistAdditionalContentItem"/> and
/// <see cref="VideoAdditionalContentItem"/> junction rows.
/// </summary>
public class AdditionalContentItem : BaseEntity<int>
{
    /// <summary>User-visible display name, editable independently of the physical file name.</summary>
    public required string FileName { get; set; }

    /// <summary>Absolute path to the file on disk.</summary>
    public required string FilePath { get; set; }

    public string? ContentType { get; set; }

    public long FileSize { get; set; }

    public DateTime UploadedAt { get; set; }

    public int ChannelId { get; set; }

    public virtual Channel Channel { get; set; } = null!;

    public virtual ICollection<PlaylistAdditionalContentItem> PlaylistLinks { get; set; } = [];

    public virtual ICollection<VideoAdditionalContentItem> VideoLinks { get; set; } = [];
}

public class AdditionalContentItemMap : IEntityTypeConfiguration<AdditionalContentItem>
{
    public void Configure(EntityTypeBuilder<AdditionalContentItem> builder)
    {
        builder.ToTable("AdditionalContent", "app");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.FileName).IsRequired().HasMaxLength(512).IsUnicode(true);
        builder.Property(m => m.FilePath).IsRequired();
        builder.Property(m => m.ContentType).HasMaxLength(128);
        builder.Property(m => m.FileSize).IsRequired();
        builder.Property(m => m.UploadedAt).IsRequired();

        builder.HasOne(m => m.Channel)
            .WithMany()
            .HasForeignKey(m => m.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
