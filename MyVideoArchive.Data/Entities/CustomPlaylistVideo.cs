using System.Runtime.Serialization;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyVideoArchive.Data.Entities;

public class CustomPlaylistVideo : IEntity
{
    public int CustomPlaylistId { get; set; }

    public int VideoId { get; set; }

    public int Order { get; set; }

    public virtual CustomPlaylist CustomPlaylist { get; set; } = null!;

    public virtual Video Video { get; set; } = null!;

    [IgnoreDataMember]
    public object[] KeyValues => [CustomPlaylistId, VideoId];
}

public class CustomPlaylistVideoMap : IEntityTypeConfiguration<CustomPlaylistVideo>
{
    public void Configure(EntityTypeBuilder<CustomPlaylistVideo> builder)
    {
        builder.ToTable("CustomPlaylistVideos", "app");
        builder.HasKey(m => new { m.CustomPlaylistId, m.VideoId });
        builder.Property(m => m.CustomPlaylistId).IsRequired();
        builder.Property(m => m.VideoId).IsRequired();
        builder.Property(m => m.Order).IsRequired().HasDefaultValue(0);

        builder.HasOne(m => m.CustomPlaylist)
            .WithMany(m => m.CustomPlaylistVideos)
            .HasForeignKey(m => m.CustomPlaylistId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Video)
            .WithMany()
            .HasForeignKey(m => m.VideoId)
            .OnDelete(DeleteBehavior.ClientNoAction);
    }
}