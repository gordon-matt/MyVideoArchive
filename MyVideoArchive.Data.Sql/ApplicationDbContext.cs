using MyVideoArchive.Data.Entities;

namespace MyVideoArchive.Data.Sql;

/// <summary>
/// SQL Server-flavoured <see cref="ApplicationDbContextBase"/>. Migrations for SQL Server live in
/// this assembly so that <c>dotnet ef</c> only ever sees one provider per project.
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : ApplicationDbContextBase(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // The shared model uses PostgreSQL's NOW() default for these columns; SQL Server has no
        // NOW() function, so use SYSUTCDATETIME() instead.
        builder.Entity<UserChannel>().Property(x => x.SubscribedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Entity<UserPlaylist>().Property(x => x.SubscribedAt).HasDefaultValueSql("SYSUTCDATETIME()");
    }
}
