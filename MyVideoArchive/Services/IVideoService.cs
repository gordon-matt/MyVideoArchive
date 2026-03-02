using Ardalis.Result;
using MyVideoArchive.Models.Api;

namespace MyVideoArchive.Services;

public interface IVideoService
{
    /// <summary>
    /// Delete the physical file for a downloaded video, clearing download metadata and marking it ignored
    /// </summary>
    Task<Result> DeleteVideoFileAsync(int channelId, int videoId);

    /// <summary>
    /// Toggle ignore status for a video
    /// </summary>
    Task<Result<bool>> ToggleIgnoreAsync(int channelId, int videoId, IgnoreVideoRequest request);
}