using System.Runtime.InteropServices;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Extenso.AspNetCore.OData;
using Hangfire;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.OData;
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

if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException(
        "This application requires a connection string. Configure ConnectionStrings:DefaultConnection via User Secrets (local), " +
        "appsettings, or environment variable ConnectionStrings__DefaultConnection (e.g. in Docker).");
}

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

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// ── Authentication provider selection ────────────────────────────────────────
// Set "Authentication:Provider" to "Keycloak" to use Keycloak instead of the
// built-in ASP.NET Core Identity system. See appsettings.json for configuration
// keys and docs/KEYCLOAK_SETUP.md for Keycloak-side setup instructions.
string authProviderName = builder.Configuration.GetValue<string>("Authentication:Provider") ?? "Identity";
bool useKeycloak = authProviderName.Equals("Keycloak", StringComparison.OrdinalIgnoreCase);

builder.Services.MvaAddAuthentication(builder.Configuration, useKeycloak);

if (useKeycloak)
{
    builder.Services.ConfigureSejil(options =>
        options.AuthenticationScheme = CookieAuthenticationDefaults.AuthenticationScheme);
}
else
{
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

builder.Services.MvaAddHangfire(connectionString);

// Register HTTP client (used by ThumbnailService)
builder.Services.AddHttpClient();

builder.Services.MvaAddServices();

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

// When using Keycloak behind a reverse proxy (HTTPS → app), process forwarded headers first
// so the app sees the original scheme (https) and host. Must run before other middleware.
if (useKeycloak)
{
    app.UseForwardedHeaders();
}

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

// When using Keycloak behind a reverse proxy, the app typically listens only on HTTP;
// the proxy terminates HTTPS. UseHttpsRedirection would try to redirect but cannot
// determine an HTTPS port, causing "Failed to determine the https port for redirect."
// Rely on the proxy and forwarded headers instead.
if (!useKeycloak)
{
    app.UseHttpsRedirection();
}

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

ScheduledTaskInitializer.Initialize();

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
    await context.Database.MigrateAsync();

    if (!useKeycloak)
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