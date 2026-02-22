using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyVideoArchive.Data.Entities;

public class Video : BaseEntity<int>
{
    public required string VideoId { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    public required string Url { get; set; }

    public string? ThumbnailUrl { get; set; }

    public required string Platform { get; set; }

    public TimeSpan? Duration { get; set; }

    public DateTime? UploadDate { get; set; }

    public int? ViewCount { get; set; }

    public int? LikeCount { get; set; }

    public DateTime? DownloadedAt { get; set; }

    public string? FilePath { get; set; }

    public long? FileSize { get; set; }

    public bool IsIgnored { get; set; }

    public bool IsQueued { get; set; }

    public bool IsManuallyImported { get; set; }

    public bool NeedsMetadataReview { get; set; }

    public int ChannelId { get; set; }

    public Channel Channel { get; set; } = null!;

    public ICollection<PlaylistVideo> PlaylistVideos { get; set; } = [];
}

public class VideoMap : IEntityTypeConfiguration<Video>
{
    public void Configure(EntityTypeBuilder<Video> builder)
    {
        builder.ToTable("Videos", "app");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.VideoId).IsRequired().HasMaxLength(128);
        builder.Property(m => m.Title).IsRequired().HasMaxLength(512).IsUnicode(true);
        builder.Property(m => m.Description).IsUnicode(true);
        builder.Property(m => m.Url).IsRequired().HasMaxLength(512);
        builder.Property(m => m.ThumbnailUrl).HasMaxLength(512);
        builder.Property(m => m.Platform).IsRequired().HasMaxLength(64);
        builder.Property(m => m.FilePath).HasMaxLength(1024);

        builder.Property(m => m.IsManuallyImported).IsRequired().HasDefaultValue(false);
        builder.Property(m => m.NeedsMetadataReview).IsRequired().HasDefaultValue(false);

        builder.HasIndex(m => new { m.Platform, m.VideoId }).IsUnique();

        builder.HasOne(m => m.Channel)
            .WithMany(m => m.Videos)
            .HasForeignKey(m => m.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}