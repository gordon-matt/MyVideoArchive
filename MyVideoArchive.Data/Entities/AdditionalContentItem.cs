using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyVideoArchive.Data.Entities;

/// <summary>
/// Represents a supplementary file (PDF, image, archive, etc.) associated with a channel,
/// optionally scoped to a playlist and/or a specific video.
/// Files are stored under the channel folder in an "_extras" subfolder; if a PlaylistId is set,
/// the file lives in "_extras/{playlist.PlaylistId}/".
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

    public int? PlaylistId { get; set; }

    public int? VideoId { get; set; }

    public virtual Channel Channel { get; set; } = null!;

    public virtual Playlist? Playlist { get; set; }

    public virtual Video? Video { get; set; }
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

        builder.HasOne(m => m.Playlist)
            .WithMany()
            .HasForeignKey(m => m.PlaylistId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(m => m.Video)
            .WithMany()
            .HasForeignKey(m => m.VideoId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
