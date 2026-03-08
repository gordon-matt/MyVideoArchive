using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace MyVideoArchive.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser, ApplicationRole, string>(options)
{
    public DbSet<Channel> Channels { get; set; }

    public DbSet<CustomPlaylist> CustomPlaylists { get; set; }

    public DbSet<CustomPlaylistVideo> CustomPlaylistVideos { get; set; }

    public DbSet<Playlist> Playlists { get; set; }

    public DbSet<Tag> Tags { get; set; }

    public DbSet<UserChannel> UserChannels { get; set; }

    public DbSet<UserPlaylist> UserPlaylists { get; set; }

    public DbSet<UserPlaylistVideo> UserPlaylistVideos { get; set; }

    public DbSet<UserSettingsEntry> UserSettings { get; set; }

    public DbSet<UserVideo> UserVideos { get; set; }

    public DbSet<Video> Videos { get; set; }

    public DbSet<VideoTag> VideoTags { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}