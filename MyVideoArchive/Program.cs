using System.Runtime.InteropServices;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Extenso.AspNetCore.OData;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.OData;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using MyVideoArchive.Data;
using MyVideoArchive.Infrastructure;
using Sejil;
using Serilog;
using Serilog.Sinks.PostgreSQL.ColumnWriters;
using Xabe.FFmpeg;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Connection string: from User Secrets (local), appsettings, or env (e.g. Docker: ConnectionStrings__DefaultConnection)
string? connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.PostgreSQL(
        connectionString: connectionString!,
        tableName: "Log",
        columnOptions: new Dictionary<string, ColumnWriterBase>
        {
            { "message", new RenderedMessageColumnWriter() },
            { "message_template", new MessageTemplateColumnWriter() },
            { "level", new LevelColumnWriter() },
            { "timestamp", new TimestampColumnWriter() },
            { "exception", new ExceptionColumnWriter() },
            { "properties", new LogEventSerializedColumnWriter() }
        },
        needAutoCreateTable: true)
    .CreateLogger();

// Use Autofac as DI container
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
builder.Host.UseSerilog();
builder.Host.UseSejil(writeToProviders: true);

if (string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseInMemoryDatabase("MyVideoArchiveDb"));
}
else
{
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(connectionString));
}

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// ── Authentication provider selection ────────────────────────────────────────
// Set "Authentication:Provider" to "Keycloak" to use Keycloak instead of the
// built-in ASP.NET Core Identity system. See appsettings.json for configuration
// keys and docs/KEYCLOAK_SETUP.md for Keycloak-side setup instructions.
string authProviderName = builder.Configuration.GetValue<string>("Authentication:Provider") ?? "Identity";
bool useKeycloak = authProviderName.Equals("Keycloak", StringComparison.OrdinalIgnoreCase);

string? keycloakAuthority = builder.Configuration["Authentication:Keycloak:Authority"];

builder.Services.AddSingleton<IAuthProviderService>(new AuthProviderService(
    useKeycloak ? AuthProvider.Keycloak : AuthProvider.Identity,
    keycloakAuthority));

if (useKeycloak)
{
    // ── Keycloak / OpenID Connect authentication ──────────────────────────────
    // A cookie keeps the server-side session; OIDC handles the external login flow.
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.LoginPath = "/auth/login";
        options.AccessDeniedPath = "/Home/AccessDenied";
    })
    .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        options.Authority = keycloakAuthority;
        options.ClientId = builder.Configuration["Authentication:Keycloak:ClientId"];
        options.ClientSecret = builder.Configuration["Authentication:Keycloak:ClientSecret"];
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;

        // Map the OIDC "preferred_username" claim to the standard Name claim.
        // The .NET JWT handler's default inbound claim type map already converts
        // the "roles" JWT claim → ClaimTypes.Role automatically, so no RoleClaimType
        // override is needed. [Authorize(Roles = "...")] and IsInRole() both check
        // ClaimTypes.Role, which is what the handler produces.
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "preferred_username"
        };

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");

        // Disable HTTPS requirement for local development (Keycloak running on HTTP).
        // In production, Keycloak should be behind HTTPS and this should be removed.
        options.RequireHttpsMetadata = false;

        // Keycloak 26+ enables Pushed Authorization Requests (PAR) by default.
        // PAR requires exact redirect URI matches (wildcards are rejected), which
        // conflicts with the wildcard URIs typically used during development.
        // Disable PAR here; it can be re-enabled once exact redirect URIs are
        // configured in Keycloak (Clients → Advanced → Pushed Authorization Requests).
        options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;

        // When the OIDC callback fails (e.g. stale/expired authorization code,
        // token endpoint error), redirect to the error page instead of re-throwing.
        // Without this, the unhandled exception causes a redirect to /Home/Error
        // which (before AllowAnonymous was added) would re-trigger a challenge,
        // creating an infinite redirect loop.
        options.Events.OnRemoteFailure = context =>
        {
            context.Response.Redirect("/Home/Error");
            context.HandleResponse();
            return Task.CompletedTask;
        };
    });

    builder.Services.ConfigureSejil(options =>
        options.AuthenticationScheme = CookieAuthenticationDefaults.AuthenticationScheme);
}
else
{
    // ── ASP.NET Core Identity (default) ──────────────────────────────────────
    builder.Services.AddIdentity<ApplicationUser, ApplicationRole>()
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders()
        .AddDefaultUI();

    builder.Services.ConfigureSejil(options =>
        options.AuthenticationScheme = IdentityConstants.ApplicationScheme);
}

builder.Services.AddControllersWithViews()
    .AddNewtonsoftJson()
    .AddOData((options, serviceProvider) =>
    {
        options.Select().Expand().Filter().OrderBy().SetMaxTop(null).Count();

        var registrars = serviceProvider.GetRequiredService<IEnumerable<IODataRegistrar>>();
        foreach (var registrar in registrars)
        {
            registrar.Register(options);
        }
    });

builder.Services.AddRazorPages();
builder.Services.AddEntityFrameworkRepository();

// Configure Hangfire (requires SQL Server)
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException(
        "Hangfire requires a connection string. Configure ConnectionStrings:DefaultConnection via User Secrets (local), " +
        "appsettings, or environment variable ConnectionStrings__DefaultConnection (e.g. in Docker).");
}

builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString),
    new PostgreSqlStorageOptions
    {
        QueuePollInterval = TimeSpan.FromSeconds(15)
    }));

// Main server: handles all queues except "downloads"
builder.Services.AddHangfireServer(options =>
{
    options.ServerName = "main";
    options.Queues = ["default", "critical"];
});

// Dedicated single-worker server for video downloads — serialises jobs and prevents
// parallel requests to YouTube that would trigger rate-limiting.
builder.Services.AddHangfireServer(options =>
{
    options.ServerName = "downloads";
    options.Queues = ["downloads"];
    options.WorkerCount = 1;
});

// Register HTTP client (used by ThumbnailService)
builder.Services.AddHttpClient();

// Register video services
builder.Services.AddSingleton<YoutubeDLInitializer>();
builder.Services.AddSingleton(sp =>
{
    var initializer = sp.GetRequiredService<YoutubeDLInitializer>();
    return initializer.GetInstanceAsync().GetAwaiter().GetResult();
});

// Register metadata providers
builder.Services.AddSingleton<IVideoMetadataProvider, YouTubeMetadataProvider>();
builder.Services.AddSingleton<IVideoMetadataProvider, BitChuteMetadataProvider>();

// Register downloaders
builder.Services.AddSingleton<IVideoDownloader, YouTubeDownloader>();
builder.Services.AddSingleton<IVideoDownloader, BitChuteDownloader>();

// Register factories
builder.Services.AddSingleton<VideoMetadataProviderFactory>();
builder.Services.AddSingleton<VideoDownloaderFactory>();

// Register thumbnail service
builder.Services.AddTransient<ThumbnailService>();

// Register Hangfire jobs
builder.Services.AddTransient<ChannelSyncJob>();
builder.Services.AddTransient<FileSystemScanJob>();
builder.Services.AddTransient<MetadataReviewJob>();
builder.Services.AddTransient<PlaylistSyncJob>();
builder.Services.AddTransient<VideoDownloadJob>();

// Register file system scan state (singleton - tracks progress across requests)
builder.Services.AddSingleton<FileSystemScanStateService>();

// Register user context service
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContextService, UserContextService>();

// Register other services
builder.Services.AddSingleton<IChannelService, ChannelService>();
builder.Services.AddSingleton<ICustomChannelService, CustomChannelService>();
builder.Services.AddSingleton<ICustomPlaylistService, CustomPlaylistService>();
builder.Services.AddSingleton<IFileSystemScanService, FileSystemScanService>();
builder.Services.AddSingleton<IPlaylistService, PlaylistService>();
builder.Services.AddSingleton<ITagService, TagService>();
builder.Services.AddSingleton<IUserSettingsService, UserSettingsService>();
builder.Services.AddSingleton<IVideoService, VideoService>();

// Configure Autofac
builder.Host.ConfigureContainer<ContainerBuilder>(containerBuilder =>
{
    containerBuilder.RegisterType<ApplicationDbContextFactory>().As<IDbContextFactory>().SingleInstance();

    containerBuilder.RegisterGeneric(typeof(EntityFrameworkRepository<>))
        .As(typeof(IRepository<>))
        .InstancePerLifetimeScope();

    containerBuilder.RegisterType<ODataRegistrar>().As<IODataRegistrar>().SingleInstance();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Serve archived thumbnails from the downloads folder under /archive (images only).
// Video/audio files are excluded because only registered image content types are served.
{
    string archivePath = app.Configuration.GetValue<string>("VideoDownload:OutputPath")
        ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

    Directory.CreateDirectory(archivePath);

    var imageContentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider(
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp",
            [".gif"] = "image/gif",
        });

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(archivePath),
        RequestPath = "/archive",
        ContentTypeProvider = imageContentTypeProvider,
        ServeUnknownFileTypes = false
    });
}

// Use odata route debug, /$odata
app.UseODataRouteDebug();

// Add OData /$query middleware
app.UseODataQueryRequest();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseSerilogRequestLogging();
app.UseSejil();

// Configure Hangfire Dashboard
app.MapHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireAuthorizationFilter()]
});

// Use fingerprinted static assets only in Development (avoids 500s in Docker when manifest paths differ)
if (app.Environment.IsDevelopment())
{
    app.MapStaticAssets();
}

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

var defaultRoute = app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
if (app.Environment.IsDevelopment())
{
    defaultRoute.WithStaticAssets();
}

var razorPages = app.MapRazorPages();
if (app.Environment.IsDevelopment())
{
    razorPages.WithStaticAssets();
}

// Schedule recurring jobs
RecurringJob.AddOrUpdate<ChannelSyncJob>(
    "sync-all-channels",
    job => job.SyncAllChannelsAsync(CancellationToken.None),
    Cron.Weekly()); // Check for new videos every day

RecurringJob.AddOrUpdate<PlaylistSyncJob>(
    "sync-all-playlists",
    job => job.SyncAllPlaylistsAsync(CancellationToken.None),
    Cron.Weekly()); // Check for new playlist videos every day

RecurringJob.AddOrUpdate<MetadataReviewJob>(
    "metadata-review",
    job => job.ExecuteAsync(CancellationToken.None),
    Cron.Weekly()); // Retry previously unavailable video metadata once per week

using var scope = app.Services.CreateScope();
var services = scope.ServiceProvider;
try
{
    var configuration = services.GetRequiredService<IConfiguration>();

    #region Xabe.Ffmpeg configuration

    bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    string ffmpegName = isWindows ? "ffmpeg.exe" : "ffmpeg";

    string? configuredFfmpegPath = configuration["YoutubeDL:FFmpegPath"];
    if (string.IsNullOrWhiteSpace(configuredFfmpegPath))
    {
        configuredFfmpegPath = null;
    }

    string ffmpegPath = configuredFfmpegPath
        ?? Path.Combine(Directory.GetCurrentDirectory(), ffmpegName);

    FFmpeg.SetExecutablesPath(ffmpegPath);

    #endregion Xabe.Ffmpeg configuration

    var context = services.GetRequiredService<ApplicationDbContext>();

    if (useKeycloak)
    {
        // When Keycloak is active, users and roles are managed externally.
        // Still run EF migrations to keep the application schema up to date.
        await context.Database.MigrateAsync();
    }
    else
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        await DbInitializer.InitializeAsync(context, userManager, roleManager, configuration);
    }
}
catch (Exception ex)
{
    var logger = services.GetRequiredService<ILogger<Program>>();
    if (logger.IsEnabled(LogLevel.Error))
    {
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

app.Run();