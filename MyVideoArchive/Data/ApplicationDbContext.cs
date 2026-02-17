using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MyVideoArchive.Data.Entities;

namespace MyVideoArchive.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
    public DbSet<Channel> Channels { get; set; }
    public DbSet<Video> Videos { get; set; }
    public DbSet<Playlist> Playlists { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfiguration(new ChannelMap());
        builder.ApplyConfiguration(new VideoMap());
        builder.ApplyConfiguration(new PlaylistMap());
        builder.ApplyConfiguration(new VideoPlaylistMap());
    }
}
