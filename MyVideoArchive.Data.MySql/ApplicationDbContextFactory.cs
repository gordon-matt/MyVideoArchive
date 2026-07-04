using Microsoft.Extensions.Configuration;

namespace MyVideoArchive.Data.MySql;

public class ApplicationDbContextFactory(IConfiguration configuration) : IDbContextFactory
{
    private DbContextOptions<ApplicationDbContext> Options
        => field ??= BuildOptions(configuration.GetConnectionString("DefaultConnection"));

    public DbContext GetContext() => new ApplicationDbContext(Options);

    public DbContext GetContext(string connectionString)
        => new ApplicationDbContext(BuildOptions(connectionString));

    private static DbContextOptions<ApplicationDbContext> BuildOptions(string? connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

        if (string.IsNullOrEmpty(connectionString))
        {
            optionsBuilder.UseInMemoryDatabase("MyVideoArchiveDb");
        }
        else
        {
            optionsBuilder.UseMySQL(connectionString, mySql =>
                mySql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name));
        }

        return optionsBuilder.Options;
    }
}
