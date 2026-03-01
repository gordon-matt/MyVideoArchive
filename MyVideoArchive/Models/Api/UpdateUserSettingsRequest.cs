namespace MyVideoArchive.Models.Api;

public class UpdateUserSettingsRequest
{
    /// <summary>
    /// "list" or "grid"
    /// </summary>
    public string? VideosTabViewMode { get; set; }

    /// <summary>
    /// "list" or "grid"
    /// </summary>
    public string? AvailableTabViewMode { get; set; }
}