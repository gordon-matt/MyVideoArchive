using System.Net;
using Hangfire;
using Heroicsoft.NetForge.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using MyVideoArchive.Data;
using MyVideoArchive.Data.MySql;
using MyVideoArchive.Data.Npgsql;
using MyVideoArchive.Data.Sql;
using MyVideoArchive.Data.Sqlite;

namespace MyVideoArchive.Infrastructure;

internal static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public void MvaAddAuthentication(IConfiguration configuration, bool useKeycloak)
        {
            services.AddNetForgeAuthentication<ApplicationUser, ApplicationRole, ApplicationDbContextBase>(
                configuration,
                options =>
                {
                    if (useKeycloak)
                    {
                        options.Provider = AuthProviderNames.Keycloak;
                    }

                    options.IncludeAuthController = false;
                    options.RegisterUserContext = false;
                    options.Identity.UseRelaxedPasswordPolicy = false;
                    options.AccessDeniedPath = "/Home/AccessDenied";
                    options.RemoteFailurePath = "/Home/Error";
                    options.Keycloak.RequireHttpsMetadata = false;
                    options.Keycloak.CorrelationCookieSameSite = SameSiteMode.None;
                    options.Keycloak.CorrelationCookieSecurePolicy = CookieSecurePolicy.Always;
                    options.Keycloak.RedirectBrowserToPublicAuthority = true;
                });

            // When behind a reverse proxy (e.g. Synology NAS), trust forwarded headers so the app
            // sees the original HTTPS scheme and host. Required for OIDC redirect URIs and Secure cookies.
            if (useKeycloak)
            {
                services.Configure<ForwardedHeadersOptions>(options =>
                {
                    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                        | ForwardedHeaders.XForwardedProto
                        | ForwardedHeaders.XForwardedHost;
                    options.KnownProxies.Clear();
                    options.KnownProxies.Add(IPAddress.Parse("127.0.0.1"));
                    options.KnownProxies.Add(IPAddress.Parse("::1"));
                    // Docker bridge: when the app runs in a container, the proxy connects from the host.
                    options.KnownProxies.Add(IPAddress.Parse("172.17.0.1"));
                });

                services.AddScoped<IUserInfoService, KeycloakUserInfoService>();
            }
            else
            {
                services.AddScoped<IUserInfoService, AspNetIdentityUserInfoService>();
            }
        }

        /// <summary>
        /// Wires up the database provider selected by <c>Database:Provider</c> in configuration.
        /// Defaults to <see cref="Constants.DatabaseProviders.Npgsql"/> to preserve behaviour for
        /// existing PostgreSQL deployments. The matching <c>MyVideoArchive.Data.{Provider}</c> project
        /// registers the concrete <c>ApplicationDbContext</c>, the <see cref="IDbContextFactory"/> and
        /// the abstract <see cref="ApplicationDbContextBase"/>.
        /// </summary>
        public IServiceCollection MvaAddDatabase(IConfiguration configuration)
        {
            string provider = configuration["Database:Provider"] ?? Constants.DatabaseProviders.Npgsql;

            return provider switch
            {
                Constants.DatabaseProviders.Npgsql => services.AddMvaNpgsql(configuration),
                Constants.DatabaseProviders.SqlServer => services.AddMvaSqlServer(configuration),
                Constants.DatabaseProviders.MySql => services.AddMvaMySql(configuration),
                Constants.DatabaseProviders.Sqlite => services.AddMvaSqlite(configuration),
                _ => throw new InvalidOperationException(
                    $"Unknown Database:Provider '{provider}'. Valid values: Npgsql, SqlServer, MySql, Sqlite.")
            };
        }

        /// <summary>
        /// Registers Hangfire backed by the same provider as the application database (selected via
        /// <c>Database:Provider</c>), then adds the shared Hangfire servers. SQLite uses a separate
        /// file (<c>Hangfire:SqlitePath</c>); the other providers reuse the application connection
        /// string unless <c>ConnectionStrings:Hangfire</c> is set.
        /// </summary>
        public void MvaAddHangfire(IConfiguration configuration)
        {
            string provider = configuration["Database:Provider"] ?? Constants.DatabaseProviders.Npgsql;
            string? hangfireExplicit = configuration.GetConnectionString("Hangfire");
            string? connectionString = string.IsNullOrWhiteSpace(hangfireExplicit)
                ? configuration.GetConnectionString("DefaultConnection")
                : hangfireExplicit;

            switch (provider)
            {
                case Constants.DatabaseProviders.Sqlite:
                    services.AddMvaSqliteHangfireStorage(configuration["Hangfire:SqlitePath"]);
                    break;

                case Constants.DatabaseProviders.SqlServer:
                    services.AddMvaSqlServerHangfireStorage(RequireHangfireConnection(connectionString, provider));
                    break;

                case Constants.DatabaseProviders.Npgsql:
                    services.AddMvaNpgsqlHangfireStorage(RequireHangfireConnection(connectionString, provider));
                    break;

                case Constants.DatabaseProviders.MySql:
                    services.AddMvaMySqlHangfireStorage(RequireHangfireConnection(connectionString, provider));
                    break;

                default:
                    throw new InvalidOperationException($"Unknown Database:Provider '{provider}'.");
            }

            // Main server: handles all queues except "downloads"
            services.AddHangfireServer(options =>
            {
                options.ServerName = "main";
                options.Queues = ["default", "critical"];
            });

            // Dedicated single-worker server for video downloads — serialises jobs and prevents
            // parallel requests to YouTube that would trigger rate-limiting.
            services.AddHangfireServer(options =>
            {
                options.ServerName = "downloads";
                options.Queues = ["downloads"];
                options.WorkerCount = 1;
            });
        }

        /// <summary>
        /// Registers the application services. The Odysee and Rumble providers are still beta and
        /// are only registered when <c>EnableBetaFeatures</c> is <c>true</c> (default <c>false</c>).
        /// Without a registered provider/downloader the platform can't be resolved anywhere (adding
        /// channels, syncing, downloading, ...), so the gate needs no checks at the call sites.
        /// </summary>
        public void MvaAddServices(IConfiguration configuration)
        {
            bool enableBetaFeatures = BetaFeatureService.IsEnabledInConfiguration(configuration);
            services.AddSingleton<IBetaFeatureService>(new BetaFeatureService(enableBetaFeatures));

            // Register video services
            services.AddSingleton<YoutubeDLInitializer>();
            services.AddSingleton(sp =>
            {
                var initializer = sp.GetRequiredService<YoutubeDLInitializer>();
                return initializer.GetInstanceAsync().GetAwaiter().GetResult();
            });

            // Register metadata providers and downloaders (stable platforms)
            services.AddSingleton<IVideoMetadataProvider, YouTubeMetadataProvider>();
            services.AddSingleton<IVideoMetadataProvider, BitChuteMetadataProvider>();
            services.AddSingleton<IVideoDownloader, YouTubeDownloader>();
            services.AddSingleton<IVideoDownloader, BitChuteDownloader>();

            if (enableBetaFeatures)
            {
                services.MvaAddBetaPlatforms();
            }

            // Register factories
            services.AddSingleton<VideoMetadataProviderFactory>();
            services.AddSingleton<VideoDownloaderFactory>();

            services.MvaAddCoreServices();
        }

        /// <summary>
        /// Odysee and Rumble providers (beta; see <see cref="BetaFeatureService"/>).
        /// </summary>
        public void MvaAddBetaPlatforms()
        {
            // Rumble scraping client: rumble.com sits behind Cloudflare bot protection that 403s
            // requests carrying only a User-Agent. A full browser-like header set (Accept,
            // Accept-Language, Sec-Fetch-*, client hints) plus gzip/br decompression (which also
            // sends Accept-Encoding) and cookie support is required for fetches to succeed.
            services.AddHttpClient(RumbleMetadataProvider.HttpClientName, client =>
            {
                client.DefaultRequestVersion = HttpVersion.Version20;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

                var headers = client.DefaultRequestHeaders;
                headers.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
                headers.TryAddWithoutValidation("Accept",
                    "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
                headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                headers.TryAddWithoutValidation("Sec-Ch-Ua", "\"Not/A)Brand\";v=\"8\", \"Chromium\";v=\"126\", \"Google Chrome\";v=\"126\"");
                headers.TryAddWithoutValidation("Sec-Ch-Ua-Mobile", "?0");
                headers.TryAddWithoutValidation("Sec-Ch-Ua-Platform", "\"Windows\"");
                headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
                headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");
                headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
                headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                UseCookies = true,
                CookieContainer = new CookieContainer()
            });

            services.AddSingleton<IVideoMetadataProvider, OdyseeMetadataProvider>();
            services.AddSingleton<IVideoMetadataProvider, RumbleMetadataProvider>();

            services.AddSingleton<IVideoDownloader, OdyseeDownloader>();
            services.AddSingleton<IVideoDownloader, RumbleDownloader>();
        }

        /// <summary>
        /// Services that are independent of which platforms are enabled.
        /// </summary>
        public void MvaAddCoreServices()
        {
            // Register thumbnail service
            services.AddTransient<ThumbnailService>();

            // Register Hangfire jobs
            services.AddTransient<ChannelSyncJob>();
            services.AddTransient<FileSystemScanJob>();
            services.AddTransient<MetadataReviewJob>();
            services.AddTransient<PlaylistSyncJob>();
            services.AddTransient<SubtitleBackfillJob>();
            services.AddTransient<TagGarbageCollectorJob>();
            services.AddTransient<VideoDownloadJob>();
            services.AddTransient<YtDlpUpdateJob>();

            // Register file system scan state (singleton - tracks progress across requests)
            services.AddSingleton<FileSystemScanStateService>();

            // Register yt-dlp maintenance (backup/update/rollback) support
            services.AddSingleton<YtDlpBackupManager>();
            services.AddSingleton<YtDlpMaintenanceStateService>();
            services.AddSingleton<IYtDlpMaintenanceService, YtDlpMaintenanceService>();

            // Register user context service
            services.AddHttpContextAccessor();
            services.AddScoped<IUserContextService, UserContextService>();

            // Register other services
            services.AddSingleton<IAdditionalContentService, AdditionalContentService>();
            services.AddSingleton<IAdminDashboardService, AdminDashboardService>();
            services.AddSingleton<IChannelService, ChannelService>();
            services.AddSingleton<ISeriesService, SeriesService>();
            services.AddSingleton<ICustomChannelService, CustomChannelService>();
            services.AddSingleton<ICustomPlaylistService, CustomPlaylistService>();
            services.AddSingleton<IFileSystemScanService, FileSystemScanService>();
            services.AddSingleton<IPlaylistService, PlaylistService>();
            services.AddSingleton<ITagService, TagService>();
            services.AddSingleton<IUserSettingsService, UserSettingsService>();
            services.AddSingleton<IVideoService, VideoService>();
        }
    }

    private static string RequireHangfireConnection(string? connectionString, string provider)
        => string.IsNullOrEmpty(connectionString)
            ? throw new InvalidOperationException($"Hangfire requires a connection string when using {provider}.")
            : connectionString;
}