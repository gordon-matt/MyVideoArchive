using Hangfire;
using Hangfire.Storage.SQLite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MyVideoArchive.Data.Sqlite;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the SQLite <see cref="ApplicationDbContext"/>, an <see cref="IDbContextFactory"/>
        /// and the abstract <see cref="ApplicationDbContextBase"/>. Falls back to EF Core's in-memory
        /// provider when no connection string is configured.
        /// </summary>
        public IServiceCollection AddMvaSqlite(IConfiguration configuration)
        {
            string? connectionString = configuration.GetConnectionString("DefaultConnection");

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                if (string.IsNullOrEmpty(connectionString))
                {
                    options.UseInMemoryDatabase("MyVideoArchiveDb");
                }
                else
                {
                    options.UseSqlite(connectionString, sqlite =>
                        sqlite.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name));
                    // SQLite ignores the "app" schema on the entity maps; silence the noisy warning.
                    options.ConfigureWarnings(w => w.Ignore(SqliteEventId.SchemaConfiguredWarning));
                }
            });

            services.AddScoped<ApplicationDbContextBase>(sp => sp.GetRequiredService<ApplicationDbContext>());
            services.AddSingleton<IDbContextFactory, ApplicationDbContextFactory>();

            return services;
        }

        /// <summary>
        /// Registers Hangfire with SQLite-backed storage. Hangfire uses a separate SQLite file so
        /// that frequent job-state writes don't contend with application data.
        /// </summary>
        public IServiceCollection AddMvaSqliteHangfireStorage(string? hangfireDbPath = null)
        {
            string path = hangfireDbPath ?? "hangfire.db";

            services.AddHangfire(config => config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseSQLiteStorage(path));

            return services;
        }
    }
}
