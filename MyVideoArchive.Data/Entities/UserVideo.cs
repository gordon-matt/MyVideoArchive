using System.Runtime.Serialization;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyVideoArchive.Data.Entities;

/// <summary>
/// Stores user-specific custom ordering for videos in playlists
/// </summary>
public class UserVideo : IEntity
{
    public required string UserId { get; set; }

    public int VideoId { get; set; }

    public bool Watched { get; set; }

    /// <summary>
    /// When true, this video is hidden from the user's video index (user-level ignore).
    /// Admins use <see cref="Video.IsIgnored"/> to block a video for all users.
    /// </summary>
    public bool IsIgnored { get; set; }

    public Video Video { get; set; } = null!;

    [IgnoreDataMember]
    public object[] KeyValues => [UserId, VideoId];
}

public class UserVideoMap : IEntityTypeConfiguration<UserVideo>
{
    public void Configure(EntityTypeBuilder<UserVideo> builder)
    {
        builder.ToTable("UserVideos", "app");

        // Composite primary key
        builder.HasKey(uv => new { uv.UserId, uv.VideoId });

        builder.HasOne(uv => uv.Video)
            .WithMany()
            .HasForeignKey(uv => uv.VideoId)
            .OnDelete(DeleteBehavior.ClientNoAction);
    }
}