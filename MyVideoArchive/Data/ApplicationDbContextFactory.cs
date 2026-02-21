namespace MyVideoArchive.Data;

public class ApplicationDbContextFactory(ISecretsManager secretsManager) : IDbContextFactory
{
    private DbContextOptions<ApplicationDbContext> Options
    {
        get
        {
            if (field is null)
            {
                var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
                string? connectionString = secretsManager.GetSecret("DefaultConnection");

                if (string.IsNullOrEmpty(connectionString))
                {
                    optionsBuilder.UseInMemoryDatabase("MyVideoArchiveDb");
                }
                else
                {
                    optionsBuilder.UseNpgsql(connectionString);
                }

                field = optionsBuilder.Options;
            }
            return field;
        }
    }

    public DbContext GetContext() => new ApplicationDbContext(Options);

    public DbContext GetContext(string connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

        if (string.IsNullOrEmpty(connectionString))
        {
            optionsBuilder.UseInMemoryDatabase("MyVideoArchiveDb");
        }
        else
        {
            optionsBuilder.UseNpgsql(connectionString);
        }

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}