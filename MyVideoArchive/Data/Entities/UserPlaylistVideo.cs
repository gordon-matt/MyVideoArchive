using System.Runtime.Serialization;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyVideoArchive.Data.Entities;

/// <summary>
/// Stores user-specific per-video settings within a playlist (custom order, visibility)
/// </summary>
public class UserPlaylistVideo : IEntity
{
    public required string UserId { get; set; }

    public int PlaylistId { get; set; }

    public int VideoId { get; set; }

    /// <summary>
    /// Custom order position set by the user (1-based). 0 means not ordered.
    /// </summary>
    public int CustomOrder { get; set; }

    /// <summary>
    /// When true, the video is hidden from the playlist for this user
    /// </summary>
    public bool IsHidden { get; set; }

    public ApplicationUser User { get; set; } = null!;

    public Playlist Playlist { get; set; } = null!;

    public Video Video { get; set; } = null!;

    [IgnoreDataMember]
    public object[] KeyValues => [UserId, PlaylistId, VideoId];
}

public class UserPlaylistVideoMap : IEntityTypeConfiguration<UserPlaylistVideo>
{
    public void Configure(EntityTypeBuilder<UserPlaylistVideo> builder)
    {
        builder.ToTable("UserPlaylistVideos", "app");

        builder.HasKey(x => new { x.UserId, x.PlaylistId, x.VideoId });

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.ClientNoAction);

        builder.HasOne(x => x.Playlist)
            .WithMany()
            .HasForeignKey(x => x.PlaylistId)
            .OnDelete(DeleteBehavior.ClientNoAction);

        builder.HasOne(x => x.Video)
            .WithMany()
            .HasForeignKey(x => x.VideoId)
            .OnDelete(DeleteBehavior.ClientNoAction);

        builder.Property(x => x.IsHidden).IsRequired();
    }
}