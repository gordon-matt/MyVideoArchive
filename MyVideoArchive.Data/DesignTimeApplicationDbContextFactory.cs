using Microsoft.EntityFrameworkCore.Design;

namespace MyVideoArchive.Data;

// This is just used for EF Migrations (Not for Production)
public class DesignTimeApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql("");
        return new ApplicationDbContext(optionsBuilder.Options);
    }
}