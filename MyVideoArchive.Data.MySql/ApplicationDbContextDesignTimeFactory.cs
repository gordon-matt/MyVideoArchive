using Microsoft.EntityFrameworkCore.Design;

namespace MyVideoArchive.Data.MySql;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> tooling so migrations can be generated without
/// needing a running MySQL instance or a fully wired-up host application.
/// </summary>
public class ApplicationDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseMySQL(
            "Server=localhost;Database=myvideoarchive_design;User=root;Password=design;",
            mySql => mySql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name));
        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
