using Microsoft.EntityFrameworkCore.Design;

namespace MyVideoArchive.Data;

public class ApplicationDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var secretsManager = new SecretsManager("\\\\DS218Plus\\homes\\Matt\\Dev\\_Secrets\\MyVideoArchive.secrets.json");
        var connectionString = secretsManager.GetSecret("DefaultConnection");
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new ApplicationDbContext(optionsBuilder.Options);
    }
}