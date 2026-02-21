using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyVideoArchive.Data.Entities;

public class Playlist : BaseEntity<int>
{
    public required string PlaylistId { get; set; }

    public required string Name { get; set; }

    public required string Url { get; set; }

    public string? Description { get; set; }

    public string? ThumbnailUrl { get; set; }

    public required string Platform { get; set; }

    public int? VideoCount { get; set; }

    public DateTime SubscribedAt { get; set; }

    public DateTime? LastChecked { get; set; }

    public bool IsIgnored { get; set; }

    public int ChannelId { get; set; }

    public Channel Channel { get; set; } = null!;

    public ICollection<PlaylistVideo> VideoPlaylists { get; set; } = [];
}

public class PlaylistMap : IEntityTypeConfiguration<Playlist>
{
    public void Configure(EntityTypeBuilder<Playlist> builder)
    {
        builder.ToTable("Playlists");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.PlaylistId).IsRequired().HasMaxLength(128);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(256).IsUnicode(true);
        builder.Property(m => m.Url).IsRequired().HasMaxLength(512);
        builder.Property(m => m.Description).IsUnicode(true);
        builder.Property(m => m.ThumbnailUrl).HasMaxLength(512);
        builder.Property(m => m.Platform).IsRequired().HasMaxLength(64);
        builder.Property(m => m.SubscribedAt).IsRequired();

        builder.HasIndex(m => new { m.Platform, m.PlaylistId }).IsUnique();

        builder.HasOne(m => m.Channel)
            .WithMany(m => m.Playlists)
            .HasForeignKey(m => m.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}