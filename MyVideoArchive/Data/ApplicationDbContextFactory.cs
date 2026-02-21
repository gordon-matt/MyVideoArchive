namespace MyVideoArchive.Data;

public class ApplicationDbContextFactory(IConfiguration configuration) : IDbContextFactory
{
    private DbContextOptions<ApplicationDbContext> Options
    {
        get
        {
            if (field is null)
            {
                var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
                string? connectionString = configuration.GetConnectionString("DefaultConnection");

                if (string.IsNullOrEmpty(connectionString))
                {
                    optionsBuilder.UseInMemoryDatabase("MyVideoArchiveDb");
                }
                else
                {
                    optionsBuilder.UseSqlServer(connectionString);
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
            optionsBuilder.UseSqlServer(connectionString);
        }

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}