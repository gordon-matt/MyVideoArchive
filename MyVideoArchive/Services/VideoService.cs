using Ardalis.Result;
using MyVideoArchive.Models.Api;

namespace MyVideoArchive.Services;

public class VideoService : IVideoService
{
    private readonly ILogger<ChannelService> logger;
    private readonly IChannelService channelService;
    private readonly IRepository<Video> videoRepository;

    public VideoService(
        ILogger<ChannelService> logger,
        IChannelService channelService,
        IRepository<Video> videoRepository)
    {
        this.logger = logger;
        this.videoRepository = videoRepository;
        this.channelService = channelService;
    }

    public async Task<Result> DeleteVideoFileAsync(int channelId, int videoId)
    {
        try
        {
            if (!await channelService.UserSubscribedToChannelAsync(channelId))
            {
                return Result.Forbidden();
            }

            var video = await videoRepository.FindOneAsync(new SearchOptions<Video>
            {
                Query = x => x.Id == videoId && x.ChannelId == channelId
            });

            if (video is null)
            {
                return Result.NotFound("Video not found");
            }

            if (!string.IsNullOrEmpty(video.FilePath) && File.Exists(video.FilePath))
            {
                File.Delete(video.FilePath);
            }

            video.DownloadedAt = null;
            video.FilePath = null;
            video.FileSize = null;
            video.IsIgnored = true;
            await videoRepository.UpdateAsync(video);

            return Result.Success();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error deleting video file for video {VideoId}", videoId);
            }

            return Result.Error("An error occurred while deleting the video file");
        }
    }

    public async Task<Result<bool>> ToggleIgnoreAsync(int channelId, int videoId, IgnoreVideoRequest request)
    {
        try
        {
            // Check user access
            if (!await channelService.UserSubscribedToChannelAsync(channelId))
            {
                return Result.Forbidden();
            }

            var video = await videoRepository.FindOneAsync(new SearchOptions<Video>
            {
                Query = x =>
                    x.Id == videoId &&
                    x.ChannelId == channelId
            });

            if (video is null)
            {
                return Result.NotFound("Video not found");
            }

            video.IsIgnored = request.IsIgnored;
            await videoRepository.UpdateAsync(video);

            return Result.Success(video.IsIgnored);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Error toggling ignore status for video {VideoId}", videoId);
            }

            return Result.Error("An error occurred while updating video status");
        }
    }
}