using Extenso.Data.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyVideoArchive.Data.Entities;

public class Channel : BaseEntity<int>
{
    public required string ChannelId { get; set; }

    public required string Name { get; set; }

    public required string Url { get; set; }

    public string? ThumbnailUrl { get; set; }

    public string? Description { get; set; }

    public required string Platform { get; set; }

    public int? SubscriberCount { get; set; }

    public int? VideoCount { get; set; }

    public DateTime SubscribedAt { get; set; }

    public DateTime? LastChecked { get; set; }

    public ICollection<Video> Videos { get; set; } = [];

    public ICollection<Playlist> Playlists { get; set; } = [];
}

public class ChannelMap : IEntityTypeConfiguration<Channel>
{
    public void Configure(EntityTypeBuilder<Channel> builder)
    {
        builder.ToTable("Channels");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.ChannelId).IsRequired().HasMaxLength(128);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(256).IsUnicode(true);
        builder.Property(m => m.Url).IsRequired().HasMaxLength(512);
        builder.Property(m => m.ThumbnailUrl).HasMaxLength(512);
        builder.Property(m => m.Description).IsUnicode(true);
        builder.Property(m => m.Platform).IsRequired().HasMaxLength(64);
        builder.Property(m => m.SubscribedAt).IsRequired();

        builder.HasIndex(m => new { m.Platform, m.ChannelId }).IsUnique();
    }
}