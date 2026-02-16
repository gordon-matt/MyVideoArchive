using Hangfire.Dashboard;

namespace MyVideoArchive.Infrastructure;

/// <summary>
/// Authorization filter for Hangfire Dashboard
/// </summary>
public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        // Allow access if user is authenticated
        // In production, you should check for specific roles or policies
        return httpContext.User.Identity?.IsAuthenticated ?? false;
    }
}
