using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyVideoArchive.Data.Entities;

public class ChannelCategory : BaseEntity<int>
{
    public required string UserId { get; set; }

    public required string Name { get; set; }

    public string? ThumbnailUrl { get; set; }
}

public class ChannelCategoryMap : IEntityTypeConfiguration<ChannelCategory>
{
    public void Configure(EntityTypeBuilder<ChannelCategory> builder)
    {
        builder.ToTable("ChannelCategories", "app");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.UserId).IsRequired();
        builder.Property(m => m.Name).IsRequired();
    }
}