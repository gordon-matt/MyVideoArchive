using System.Runtime.Serialization;
using Extenso.Data.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyVideoArchive.Data.Entities;

/// <summary>
/// Junction table for many-to-many relationship between Users and Channels
/// Represents a user's subscription to a channel
/// </summary>
public class UserChannel : IEntity
{
    public required string UserId { get; set; }

    public int ChannelId { get; set; }

    public DateTime SubscribedAt { get; set; }

    public ApplicationUser User { get; set; } = null!;

    public Channel Channel { get; set; } = null!;

    [IgnoreDataMember]
    public object[] KeyValues => [UserId, ChannelId];
}

public class UserChannelMap : IEntityTypeConfiguration<UserChannel>
{
    public void Configure(EntityTypeBuilder<UserChannel> builder)
    {
        builder.ToTable("UserChannels");

        // Composite primary key
        builder.HasKey(uc => new { uc.UserId, uc.ChannelId });

        // Relationships
        builder.HasOne(uc => uc.User)
            .WithMany()
            .HasForeignKey(uc => uc.UserId)
            .OnDelete(DeleteBehavior.ClientNoAction);

        builder.HasOne(uc => uc.Channel)
            .WithMany()
            .HasForeignKey(uc => uc.ChannelId)
            .OnDelete(DeleteBehavior.ClientNoAction);

        // Default value for SubscribedAt
        builder.Property(uc => uc.SubscribedAt)
            .HasDefaultValueSql("GETUTCDATE()");
    }
}