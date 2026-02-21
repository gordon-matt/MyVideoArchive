using System.Runtime.Serialization;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyVideoArchive.Data.Entities;

/// <summary>
/// Stores user-specific custom ordering for videos in playlists
/// </summary>
public class UserVideoOrder : IEntity
{
    public required string UserId { get; set; }

    public int PlaylistId { get; set; }

    public int VideoId { get; set; }

    /// <summary>
    /// Custom order position set by the user (1-based)
    /// </summary>
    public int CustomOrder { get; set; }

    public ApplicationUser User { get; set; } = null!;

    public Playlist Playlist { get; set; } = null!;

    public Video Video { get; set; } = null!;

    [IgnoreDataMember]
    public object[] KeyValues => [UserId, PlaylistId, VideoId];
}

public class UserVideoOrderMap : IEntityTypeConfiguration<UserVideoOrder>
{
    public void Configure(EntityTypeBuilder<UserVideoOrder> builder)
    {
        builder.ToTable("UserVideoOrders", "app");

        // Composite primary key
        builder.HasKey(uvo => new { uvo.UserId, uvo.PlaylistId, uvo.VideoId });

        // Relationships
        builder.HasOne(uvo => uvo.User)
            .WithMany()
            .HasForeignKey(uvo => uvo.UserId)
            .OnDelete(DeleteBehavior.ClientNoAction);

        builder.HasOne(uvo => uvo.Playlist)
            .WithMany()
            .HasForeignKey(uvo => uvo.PlaylistId)
            .OnDelete(DeleteBehavior.ClientNoAction);

        builder.HasOne(uvo => uvo.Video)
            .WithMany()
            .HasForeignKey(uvo => uvo.VideoId)
            .OnDelete(DeleteBehavior.ClientNoAction);
    }
}