using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyVideoArchive.Data.Enums;

namespace MyVideoArchive.Data.Entities;

public class UserSettingsEntry : BaseEntity<int>
{
    public required string UserId { get; set; }

    public ViewMode VideosTabViewMode { get; set; }

    public ViewMode AvailableTabViewMode { get; set; }

    public virtual ApplicationUser User { get; set; } = null!;
}

public class UserSettingsEntryMap : IEntityTypeConfiguration<UserSettingsEntry>
{
    public void Configure(EntityTypeBuilder<UserSettingsEntry> builder)
    {
        builder.ToTable("UserSettings", "app");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.UserId).IsRequired();
        builder.Property(m => m.VideosTabViewMode).IsRequired();
        builder.Property(m => m.AvailableTabViewMode).IsRequired();

        // Relationships
        builder.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.ClientNoAction);
    }
}