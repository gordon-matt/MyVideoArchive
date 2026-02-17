using Autofac;
using Autofac.Extensions.DependencyInjection;
using Extenso.AspNetCore.OData;
using Extenso.Data.Entity;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.OData;
using Microsoft.EntityFrameworkCore;
using MyVideoArchive.Data;
using MyVideoArchive.Data.Entities;
using MyVideoArchive.Infrastructure;
using MyVideoArchive.Services;
using MyVideoArchive.Services.Abstractions;
using MyVideoArchive.Services.Jobs;
using MyVideoArchive.Services.Providers;
using YoutubeDLSharp;

var builder = WebApplication.CreateBuilder(args);

// Use Autofac as DI container
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseInMemoryDatabase("MyVideoArchiveDb"));
}
else
{
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlServer(connectionString));
}

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
})
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
    throw new InvalidOperationException("Hangfire requires a SQL Server connection string. Please configure 'DefaultConnection' in appsettings.json.");
}

builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
    {
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        QueuePollInterval = TimeSpan.Zero,
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true
    }));

builder.Services.AddHangfireServer();

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

// Register Hangfire jobs
builder.Services.AddTransient<VideoDownloadJob>();
builder.Services.AddTransient<ChannelSyncJob>();
builder.Services.AddTransient<PlaylistSyncJob>();

// Register user context service
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContextService, UserContextService>();

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
    Authorization = new[] { new HangfireAuthorizationFilter() }
});

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

// Schedule recurring jobs
RecurringJob.AddOrUpdate<ChannelSyncJob>(
    "sync-all-channels",
    job => job.SyncAllChannelsAsync(CancellationToken.None),
    Cron.Daily); // Check for new videos every day

RecurringJob.AddOrUpdate<PlaylistSyncJob>(
    "sync-all-playlists",
    job => job.SyncAllPlaylistsAsync(CancellationToken.None),
    Cron.Daily); // Check for new playlist videos every day


if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();

        await DbInitializer.InitializeAsync(context, userManager, roleManager);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

app.Run();
