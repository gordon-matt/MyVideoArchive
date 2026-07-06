using Ardalis.Result;
using MyVideoArchive.Models.Responses;

namespace MyVideoArchive.Services;

public interface IAdminDashboardService
{
    /// <summary>
    /// Aggregate archive-wide statistics for the admin dashboard (home page for administrators).
    /// </summary>
    Task<Result<AdminDashboardStatsResponse>> GetStatsAsync(CancellationToken cancellationToken = default);
}
