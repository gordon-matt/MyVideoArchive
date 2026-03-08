using Ardalis.Result;
using MyVideoArchive.Models.Requests;
using MyVideoArchive.Models.Responses;

namespace MyVideoArchive.Services;

public interface IUserSettingsService
{
    /// <summary>
    /// Get the current user's UI settings
    /// </summary>
    Task<Result<GetUserSettingsResponse>> GetSettingsAsync();

    /// <summary>
    /// Update the current user's UI settings
    /// </summary>
    Task<Result> UpdateSettingsAsync(UpdateUserSettingsRequest request);
}