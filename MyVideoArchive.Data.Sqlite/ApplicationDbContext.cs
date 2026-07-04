using MyVideoArchive.Data.Entities;

namespace MyVideoArchive.Data.Sqlite;

/// <summary>
/// SQLite-flavoured <see cref="ApplicationDbContextBase"/>. Migrations for SQLite live in
/// this assembly so that <c>dotnet ef</c> only ever sees one provider per project.
/// SQLite has no concept of schemas; the "app" schema on the entity maps is ignored and the
/// resulting <c>SchemaConfiguredWarning</c> is silenced where the context is configured.
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : ApplicationDbContextBase(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // The shared model uses PostgreSQL's NOW() default for these columns; SQLite has no NOW()
        // function, so use CURRENT_TIMESTAMP instead.
        builder.Entity<UserChannel>().Property(x => x.SubscribedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Entity<UserPlaylist>().Property(x => x.SubscribedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}
