using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MyVideoArchive.Data.Entities;

namespace MyVideoArchive.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser, ApplicationRole, string>(options)
{
    public DbSet<Channel> Channels { get; set; }
    public DbSet<Video> Videos { get; set; }
    public DbSet<Playlist> Playlists { get; set; }
    public DbSet<UserChannel> UserChannels { get; set; }
    public DbSet<UserPlaylist> UserPlaylists { get; set; }
    public DbSet<UserVideoOrder> UserVideoOrders { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}