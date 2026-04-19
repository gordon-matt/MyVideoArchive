using System.Runtime.InteropServices;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using ElectronNET;
using ElectronNET.API;
using ElectronNET.API.Entities;
using Extenso.AspNetCore.OData;
using Hangfire;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.OData;
using Microsoft.Extensions.FileProviders;
using MyVideoArchive.Data;
using MyVideoArchive.Desktop;
using MyVideoArchive.Infrastructure;
using Sejil;
using Serilog;
using Serilog.Sinks.PostgreSQL.ColumnWriters;
using Xabe.FFmpeg;

// ────────────────────────────────────────────────────────────────────────────────
// Desktop entry point. This is the Electron-wrapped variant of MyVideoArchive.
// The web/Docker version (MyVideoArchive/Program.cs) is intentionally untouched.
// All controllers, views, services and static assets are shared via linked items
// in MyVideoArchive.Desktop.csproj.
// ────────────────────────────────────────────────────────────────────────────────

// During `dotnet run` this project has no wwwroot of its own (static assets are
// shared with the MyVideoArchive web project via linked Content items in the
// .csproj). The locator returns the sibling MyVideoArchive\wwwroot in dev and
// falls back to <exe>\wwwroot for published builds (where MSBuild has copied the
// linked Content items into the publish output). Either way the returned path
// is guaranteed to exist, so the default static-files PhysicalFileProvider
// won't throw at startup.
string webRootPath = SharedAssetLocator.ResolveWebRoot();

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = webRootPath
});

// Connection string: from User Secrets (local), appsettings, or env (e.g. Docker: ConnectionStrings__DefaultConnection)
string? connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException(
        "MyVideoArchive.Desktop requires a connection string. Configure ConnectionStrings:DefaultConnection " +
        "via User Secrets, appsettings.json next to the executable, or an environment variable " +
        "(ConnectionStrings__DefaultConnection).");
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

builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
builder.Host.UseSerilog();
builder.Host.UseSejil(writeToProviders: true);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

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

builder.Services.AddHttpClient();

builder.Services.MvaAddServices();

// Register Electron services and wire up the desktop window. UseElectron is a no-op
// when launched as a regular ASP.NET Core process (e.g. `dotnet run`), so this same
// project can also run headless during development.
builder.Services.AddElectron();
builder.UseElectron(args, OnElectronAppReadyAsync);

builder.Host.ConfigureContainer<ContainerBuilder>(containerBuilder =>
{
    containerBuilder.RegisterType<ApplicationDbContextFactory>().As<IDbContextFactory>().SingleInstance();

    containerBuilder.RegisterGeneric(typeof(EntityFrameworkRepository<>))
        .As(typeof(IRepository<>))
        .InstancePerLifetimeScope();

    containerBuilder.RegisterType<ODataRegistrar>().As<IODataRegistrar>().SingleInstance();
});

var app = builder.Build();

if (useKeycloak)
{
    app.UseForwardedHeaders();
}

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

if (!useKeycloak)
{
    app.UseHttpsRedirection();
}

// In dev runs the WebRootPath above already points at the shared wwwroot. When the
// app is packaged via Electron, content lands in the publish output's wwwroot.
app.UseStaticFiles();

{
    string archivePath = app.Configuration.GetValue<string>("VideoDownload:OutputPath")
        ?? Path.Combine(AppContext.BaseDirectory, "Downloads");

    // PhysicalFileProvider requires an absolute path. Resolve relative paths
    // (e.g. the "Downloads" default in appsettings.json) against the executable
    // folder so the desktop build behaves predictably regardless of where it
    // was launched from.
    if (!Path.IsPathRooted(archivePath))
    {
        archivePath = Path.GetFullPath(archivePath, AppContext.BaseDirectory);
    }

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
        FileProvider = new PhysicalFileProvider(archivePath),
        RequestPath = "/archive",
        ContentTypeProvider = imageContentTypeProvider,
        ServeUnknownFileTypes = false
    });
}

app.UseODataRouteDebug();
app.UseODataQueryRequest();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.UseSerilogRequestLogging();
app.UseSejil();

app.MapHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireAuthorizationFilter()]
});

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

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var configuration = services.GetRequiredService<IConfiguration>();

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
}

app.Run();

// ──────────────────────────────────────────────────────────────────────────────
// Electron callback — invoked by ElectronNET.Core once the host is ready. Opens
// the main browser window pointed at the in-process ASP.NET Core server.
// ──────────────────────────────────────────────────────────────────────────────
static async Task OnElectronAppReadyAsync()
{
    var options = new BrowserWindowOptions
    {
        Show = false,
        Width = 1400,
        Height = 900,
        Title = "MyVideoArchive"
    };

    if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
    {
        options.AutoHideMenuBar = true;
    }

    var window = await Electron.WindowManager.CreateWindowAsync(options);

    window.OnReadyToShow += () => window.Show();
    window.OnClosed += () => Electron.App.Quit();
}

