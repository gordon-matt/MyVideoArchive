using Autofac;
using Autofac.Extensions.DependencyInjection;
using Extenso.AspNetCore.OData;
using Extenso.Data.Entity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.OData;
using Microsoft.EntityFrameworkCore;
using MyVideoArchive.Data;
using MyVideoArchive.Infrastructure;
using MyVideoArchive.Services;

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

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddEntityFrameworkStores<ApplicationDbContext>();

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

// Add background service
builder.Services.AddHostedService<VideoDownloadService>();

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

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

app.Run();
