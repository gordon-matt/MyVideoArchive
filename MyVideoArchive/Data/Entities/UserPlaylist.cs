using System.Runtime.Serialization;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyVideoArchive.Data.Entities;

/// <summary>
/// Junction table for many-to-many relationship between Users and Playlists
/// Represents a user's subscription to a playlist
/// </summary>
public class UserPlaylist : IEntity
{
    public required string UserId { get; set; }

    public int PlaylistId { get; set; }

    public DateTime SubscribedAt { get; set; }

    public ApplicationUser User { get; set; } = null!;

    public Playlist Playlist { get; set; } = null!;

    [IgnoreDataMember]
    public object[] KeyValues => [UserId, PlaylistId];
}

public class UserPlaylistMap : IEntityTypeConfiguration<UserPlaylist>
{
    public void Configure(EntityTypeBuilder<UserPlaylist> builder)
    {
        builder.ToTable("UserPlaylists");

        // Composite primary key
        builder.HasKey(up => new { up.UserId, up.PlaylistId });

        // Relationships
        builder.HasOne(up => up.User)
            .WithMany()
            .HasForeignKey(up => up.UserId)
            .OnDelete(DeleteBehavior.ClientNoAction);

        builder.HasOne(up => up.Playlist)
            .WithMany()
            .HasForeignKey(up => up.PlaylistId)
            .OnDelete(DeleteBehavior.ClientNoAction);

        // Default value for SubscribedAt
        builder.Property(up => up.SubscribedAt)
            .HasDefaultValueSql("GETUTCDATE()");
    }
}