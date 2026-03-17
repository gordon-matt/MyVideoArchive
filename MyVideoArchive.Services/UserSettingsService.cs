using Ardalis.Result;
using MyVideoArchive.Data.Enums;
using MyVideoArchive.Models.Requests;
using MyVideoArchive.Models.Responses;

namespace MyVideoArchive.Services;

public class UserSettingsService : IUserSettingsService
{
    private readonly ILogger<UserSettingsService> logger;
    private readonly IUserContextService userContextService;
    private readonly IRepository<UserSettingsEntry> userSettingsRepository;

    public UserSettingsService(
        ILogger<UserSettingsService> logger,
        IUserContextService userContextService,
        IRepository<UserSettingsEntry> userSettingsRepository)
    {
        this.logger = logger;
        this.userContextService = userContextService;
        this.userSettingsRepository = userSettingsRepository;
    }

    public async Task<Result<GetUserSettingsResponse>> GetSettingsAsync()
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            var entry = await userSettingsRepository.FindOneAsync(new SearchOptions<UserSettingsEntry>
            {
                Query = x => x.UserId == userId
            });

            string videosTabViewMode = entry?.VideosTabViewMode.ToString().ToLowerInvariant() ?? "list";
            string availableTabViewMode = entry?.AvailableTabViewMode.ToString().ToLowerInvariant() ?? "list";
            bool enableChannelCategories = entry?.EnableChannelCategories ?? false;

            return Result.Success(new GetUserSettingsResponse(videosTabViewMode, availableTabViewMode, enableChannelCategories));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error retrieving user settings");
            }

            return Result.Error("An error occurred while retrieving user settings");
        }
    }

    public async Task<Result> UpdateSettingsAsync(UpdateUserSettingsRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            var entry = await userSettingsRepository.FindOneAsync(new SearchOptions<UserSettingsEntry>
            {
                Query = x => x.UserId == userId
            });

            if (entry is null)
            {
                entry = new UserSettingsEntry
                {
                    UserId = userId,
                    VideosTabViewMode = ParseViewMode(request.VideosTabViewMode),
                    AvailableTabViewMode = ParseViewMode(request.AvailableTabViewMode),
                    EnableChannelCategories = request.EnableChannelCategories ?? false
                };
                await userSettingsRepository.InsertAsync(entry);
            }
            else
            {
                if (request.VideosTabViewMode is not null)
                {
                    entry.VideosTabViewMode = ParseViewMode(request.VideosTabViewMode);
                }

                if (request.AvailableTabViewMode is not null)
                {
                    entry.AvailableTabViewMode = ParseViewMode(request.AvailableTabViewMode);
                }

                if (request.EnableChannelCategories is not null)
                {
                    entry.EnableChannelCategories = request.EnableChannelCategories.Value;
                }

                await userSettingsRepository.UpdateAsync(entry);
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error updating user settings");
            }

            return Result.Error("An error occurred while updating user settings");
        }
    }

    private static ViewMode ParseViewMode(string? value) =>
        value?.ToLowerInvariant() == "grid" ? ViewMode.Grid : ViewMode.List;
}