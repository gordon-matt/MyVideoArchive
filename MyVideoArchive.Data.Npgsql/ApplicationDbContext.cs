namespace MyVideoArchive.Data.Npgsql;

/// <summary>
/// PostgreSQL-flavoured <see cref="ApplicationDbContextBase"/>. Migrations for PostgreSQL live in
/// this assembly so that <c>dotnet ef</c> only ever sees one provider per project.
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : ApplicationDbContextBase(options)
{
}
