using Microsoft.AspNetCore.Identity;

namespace MyVideoArchive.Data;

/// <summary>
/// Database initializer for seeding roles and default admin user
/// </summary>
public static class DbInitializer
{
    /// <summary>
    /// Configuration keys for initial admin user (override via User Secrets or env e.g. SeedAdmin__Email, SeedAdmin__Password).
    /// </summary>
    public const string SeedAdminEmailKey = "SeedAdmin:Email";

    public const string SeedAdminPasswordKey = "SeedAdmin:Password";

    public static async Task InitializeAsync(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IConfiguration configuration)
    {
        // Ensure database is created
        await context.Database.MigrateAsync();

        // Seed roles
        bool isFirstRun = await SeedRolesAsync(roleManager);
        if (isFirstRun)
        {
            // Seed default admin user
            await SeedAdminUserAsync(userManager, configuration);
        }
    }

    private static async Task<bool> SeedRolesAsync(RoleManager<ApplicationRole> roleManager)
    {
        string[] roleNames = [Constants.Roles.Administrator, Constants.Roles.User];

        bool isFirstRun = false;

        foreach (string roleName in roleNames)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                isFirstRun = true;
                await roleManager.CreateAsync(new ApplicationRole { Name = roleName });
            }
        }

        return isFirstRun;
    }

    private static async Task SeedAdminUserAsync(UserManager<ApplicationUser> userManager, IConfiguration configuration)
    {
        string adminEmail = string.IsNullOrEmpty(configuration[SeedAdminEmailKey])
            ? "admin@myvideoarchive.local"
            : configuration[SeedAdminEmailKey]!;

        string adminPassword = string.IsNullOrEmpty(configuration[SeedAdminPasswordKey])
            ? "Admin@123"
            : configuration[SeedAdminPasswordKey]!;

        var adminUser = await userManager.FindByEmailAsync(adminEmail);

        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(adminUser, adminPassword);

            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, Constants.Roles.Administrator);
            }
        }
        else
        {
            // Ensure existing admin user has the Administrator role
            if (!await userManager.IsInRoleAsync(adminUser, Constants.Roles.Administrator))
            {
                await userManager.AddToRoleAsync(adminUser, Constants.Roles.Administrator);
            }
        }
    }
}