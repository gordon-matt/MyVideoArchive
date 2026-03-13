using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyVideoArchive.Data.Entities;

public class CustomPlaylist : BaseEntity<int>
{
    public required string UserId { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public string? ThumbnailUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<CustomPlaylistVideo> CustomPlaylistVideos { get; set; } = [];

    public ICollection<CustomPlaylistTag> CustomPlaylistTags { get; set; } = [];
}

public class CustomPlaylistMap : IEntityTypeConfiguration<CustomPlaylist>
{
    public void Configure(EntityTypeBuilder<CustomPlaylist> builder)
    {
        builder.ToTable("CustomPlaylists", "app");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(256).IsUnicode(true);
        builder.Property(m => m.Description).IsUnicode(true);
        builder.Property(m => m.ThumbnailUrl);
        builder.Property(m => m.CreatedAt).IsRequired();
    }
}