using Microsoft.EntityFrameworkCore.Design;

namespace MyVideoArchive.Data;

/// <summary>
/// Used by EF Core tools (e.g. migrations). Connection string is read from environment variable
/// ConnectionStrings__DefaultConnection or from appsettings.Development.json in the project directory.
/// For local dev, use User Secrets: dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=...;..."
/// </summary>
public class ApplicationDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        string basePath = Directory.GetCurrentDirectory();
        var config = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .AddUserSecrets<ApplicationDesignTimeDbContextFactory>(optional: true)
            .Build();

        string connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Set ConnectionStrings:DefaultConnection (e.g. via User Secrets or env ConnectionStrings__DefaultConnection).");

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new ApplicationDbContext(optionsBuilder.Options);
    }
}