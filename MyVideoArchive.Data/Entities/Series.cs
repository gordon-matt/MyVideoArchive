using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyVideoArchive.Data.Entities;

public class Series : BaseEntity<int>
{
    public required string Name { get; set; }

    public int ChannelId { get; set; }

    public virtual Channel Channel { get; set; } = null!;

    public virtual ICollection<SeriesPlaylist> SeriesPlaylists { get; set; } = [];
}

public class SeriesMap : IEntityTypeConfiguration<Series>
{
    public void Configure(EntityTypeBuilder<Series> builder)
    {
        builder.ToTable("Series", "app");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(256).IsUnicode(true);

        builder.HasOne(m => m.Channel)
            .WithMany()
            .HasForeignKey(m => m.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}