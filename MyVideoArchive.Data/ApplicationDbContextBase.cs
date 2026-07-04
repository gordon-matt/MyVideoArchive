using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace MyVideoArchive.Data;

/// <summary>
/// Provider-agnostic <see cref="DbContext"/> shared by all <c>MyVideoArchive.Data.{Provider}</c>
/// projects. Each provider project inherits from this class and supplies its own provider
/// configuration / migrations assembly. All <see cref="IEntityTypeConfiguration{TEntity}"/>
/// classes that live next to the entities in this assembly are picked up automatically.
/// Provider-specific overrides (e.g. schema flattening for MySQL) can be applied by overriding
/// <see cref="OnModelCreating"/> in the subclass and calling <c>base.OnModelCreating(builder)</c>.
/// </summary>
public abstract class ApplicationDbContextBase : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    protected ApplicationDbContextBase(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<Channel> Channels { get; set; }

    public DbSet<ChannelCategory> ChannelCategories { get; set; }

    public DbSet<ChannelTag> ChannelTags { get; set; }

    public DbSet<CustomPlaylist> CustomPlaylists { get; set; }

    public DbSet<CustomPlaylistTag> CustomPlaylistTags { get; set; }

    public DbSet<CustomPlaylistVideo> CustomPlaylistVideos { get; set; }

    public DbSet<Playlist> Playlists { get; set; }

    public DbSet<PlaylistTag> PlaylistTags { get; set; }

    public DbSet<Tag> Tags { get; set; }

    public DbSet<UserChannel> UserChannels { get; set; }

    public DbSet<UserPlaylist> UserPlaylists { get; set; }

    public DbSet<UserPlaylistVideo> UserPlaylistVideos { get; set; }

    public DbSet<UserSettingsEntry> UserSettings { get; set; }

    public DbSet<UserVideo> UserVideos { get; set; }

    public DbSet<AdditionalContentItem> AdditionalContent { get; set; }

    public DbSet<PlaylistAdditionalContentItem> PlaylistAdditionalContent { get; set; }

    public DbSet<VideoAdditionalContentItem> VideoAdditionalContent { get; set; }

    public DbSet<Video> Videos { get; set; }

    public DbSet<VideoTag> VideoTags { get; set; }

    public DbSet<Series> Series { get; set; }

    public DbSet<SeriesPlaylist> SeriesPlaylists { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContextBase).Assembly);
    }
}
