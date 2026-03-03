using Autofac;
using Autofac.Extensions.DependencyInjection;
using Extenso.AspNetCore.OData;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.OData;
using MyVideoArchive.Data;
using MyVideoArchive.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Use Autofac as DI container
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());

// Add services to the container.
// Connection string: from User Secrets (local), appsettings, or env (e.g. Docker: ConnectionStrings__DefaultConnection)
string? connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

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

builder.Services.AddIdentity<ApplicationUser, ApplicationRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders()
    .AddDefaultUI();

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

builder.Services.AddHangfireServer();

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

// Register downloaders
builder.Services.AddSingleton<IVideoDownloader, YouTubeDownloader>();

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

// Use odata route debug, /$odata
app.UseODataRouteDebug();

// Add OData /$query middleware
app.UseODataQueryRequest();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

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
    var context = services.GetRequiredService<ApplicationDbContext>();
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
    var configuration = services.GetRequiredService<IConfiguration>();

    await DbInitializer.InitializeAsync(context, userManager, roleManager, configuration);
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