using System.Runtime.Serialization;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyVideoArchive.Data.Entities;

/// <summary>
/// Junction: an additional-content file may be associated with zero or many playlists on the same channel.
/// </summary>
public class PlaylistAdditionalContentItem : IEntity
{
    public int PlaylistId { get; set; }

    public int AdditionalContentItemId { get; set; }

    public virtual Playlist Playlist { get; set; } = null!;

    public virtual AdditionalContentItem AdditionalContentItem { get; set; } = null!;

    [IgnoreDataMember]
    public object[] KeyValues => [PlaylistId, AdditionalContentItemId];
}

public class PlaylistAdditionalContentItemMap : IEntityTypeConfiguration<PlaylistAdditionalContentItem>
{
    public void Configure(EntityTypeBuilder<PlaylistAdditionalContentItem> builder)
    {
        builder.ToTable("PlaylistAdditionalContent", "app");
        builder.HasKey(x => new { x.PlaylistId, x.AdditionalContentItemId });

        builder.HasOne(x => x.Playlist)
            .WithMany(p => p.PlaylistAdditionalContentItems)
            .HasForeignKey(x => x.PlaylistId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.AdditionalContentItem)
            .WithMany(a => a.PlaylistLinks)
            .HasForeignKey(x => x.AdditionalContentItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
