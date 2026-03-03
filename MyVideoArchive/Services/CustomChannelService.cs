using Ardalis.Result;
using MyVideoArchive.Models.Requests.Channel;
using MyVideoArchive.Models.Responses;

namespace MyVideoArchive.Services;

public class CustomChannelService : ICustomChannelService
{
    private static readonly string[] AllowedImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    private readonly ILogger<CustomChannelService> logger;
    private readonly IConfiguration configuration;
    private readonly IUserContextService userContextService;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<Playlist> playlistRepository;
    private readonly IRepository<PlaylistVideo> playlistVideoRepository;
    private readonly IRepository<UserChannel> userChannelRepository;
    private readonly IRepository<Video> videoRepository;

    public CustomChannelService(
        ILogger<CustomChannelService> logger,
        IConfiguration configuration,
        IUserContextService userContextService,
        IRepository<Channel> channelRepository,
        IRepository<Playlist> playlistRepository,
        IRepository<PlaylistVideo> playlistVideoRepository,
        IRepository<UserChannel> userChannelRepository,
        IRepository<Video> videoRepository)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.userContextService = userContextService;
        this.channelRepository = channelRepository;
        this.playlistRepository = playlistRepository;
        this.playlistVideoRepository = playlistVideoRepository;
        this.userChannelRepository = userChannelRepository;
        this.videoRepository = videoRepository;
    }

    public async Task<Result<CreateChannelResponse>> CreateChannelAsync(CreateCustomChannelRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result.Unauthorized();
            }

            string channelId = Guid.NewGuid().ToString("N");
            var channel = new Channel
            {
                ChannelId = channelId,
                Name = request.Name,
                Description = request.Description,
                Url = $"custom://{channelId}",
                Platform = "Custom",
                SubscribedAt = DateTime.UtcNow
            };

            await channelRepository.InsertAsync(channel);
            await userChannelRepository.InsertAsync(new UserChannel
            {
                UserId = userId,
                ChannelId = channel.Id,
                SubscribedAt = DateTime.UtcNow
            });

            logger.LogInformation("Created custom channel {ChannelId} for user {UserId}", channel.Id, userId);

            return Result.Success(new CreateChannelResponse(channel.Id, channel.Name, channel.Platform));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating custom channel");
            return Result.Error("An error occurred while creating the custom channel");
        }
    }

    public async Task<Result<CreateChannelPlaylistResponse>> CreatePlaylistAsync(int channelId, CreateCustomChannelPlaylistRequest request)
    {
        var (canAccess, channel) = await CanAccessChannelAsync(channelId);
        if (!canAccess)
        {
            return Result.Forbidden();
        }

        if (channel is null || channel.Platform != "Custom")
        {
            return Result.NotFound("Channel not found");
        }

        string playlistId = Guid.NewGuid().ToString("N");
        var playlist = new Playlist
        {
            PlaylistId = playlistId,
            Name = request.Name,
            Description = request.Description,
            Url = $"custom://{playlistId}",
            Platform = "Custom",
            ChannelId = channelId,
            SubscribedAt = DateTime.UtcNow
        };

        await playlistRepository.InsertAsync(playlist);
        logger.LogInformation("Created custom playlist {PlaylistId} in channel {ChannelId}", playlist.Id, channelId);
        return Result.Success(new CreateChannelPlaylistResponse(playlist.Id, playlist.Name));
    }

    public async Task<Result> DeletePlaylistAsync(int playlistId)
    {
        var playlist = await playlistRepository.FindOneAsync(playlistId);
        if (playlist is null)
        {
            return Result.NotFound("Playlist not found");
        }

        var (canAccess, _) = await CanAccessChannelAsync(playlist.ChannelId);
        if (!canAccess)
        {
            return Result.Forbidden();
        }

        var playlistVideos = await playlistVideoRepository.FindAsync(new SearchOptions<PlaylistVideo>
        {
            Query = x => x.PlaylistId == playlistId
        });
        await playlistVideoRepository.DeleteAsync(playlistVideos);
        await TryDeletePlaylistThumbnailFileAsync(playlist);
        await playlistRepository.DeleteAsync(playlist);
        logger.LogInformation("Deleted custom playlist {PlaylistId}", playlistId);
        return Result.Success();
    }

    public async Task<Result<GetChannelPlaylistsResponse>> GetChannelPlaylistsAsync(int channelId)
    {
        var (canAccess, channel) = await CanAccessChannelAsync(channelId);
        if (!canAccess)
        {
            return Result.Forbidden();
        }

        if (channel is null || channel.Platform != "Custom")
        {
            return Result.NotFound("Channel not found");
        }

        var playlists = await playlistRepository.FindAsync(new SearchOptions<Playlist>
        {
            Query = x => x.ChannelId == channelId && !x.IsIgnored
        }, x => new ChannelPlaylistItem(x.Id, x.Name));

        return Result.Success(new GetChannelPlaylistsResponse(playlists.ToList()));
    }

    public async Task<Result<ThumbnailFileInfo>> GetChannelThumbnailAsync(int channelId)
    {
        var (canAccess, channel) = await CanAccessChannelAsync(channelId);
        if (!canAccess)
        {
            return Result.Forbidden();
        }

        if (channel is null || channel.Platform != "Custom")
        {
            return Result.NotFound("Channel not found");
        }

        string dir = GetCustomChannelThumbnailDirectory(channel);
        var info = ResolveThumbnail(dir, "channel");
        return info is null ? Result.NotFound("Thumbnail not found") : Result.Success(info);
    }

    public async Task<Result<ThumbnailFileInfo>> GetPlaylistThumbnailAsync(int playlistId)
    {
        var playlist = await playlistRepository.FindOneAsync(new SearchOptions<Playlist>
        {
            Query = x => x.Id == playlistId,
            Include = query => query.Include(x => x.Channel)
        });
        if (playlist is null)
        {
            return Result.NotFound("Playlist not found");
        }

        string dir = GetPlaylistThumbnailDirectory(playlist);
        var info = ResolveThumbnail(dir, playlist.PlaylistId);
        return info is null ? Result.NotFound("Thumbnail not found") : Result.Success(info);
    }

    public async Task<Result<GetVideoPlaylistIdsResponse>> GetVideoPlaylistIdsAsync(int videoId)
    {
        var video = await videoRepository.FindOneAsync(videoId);
        if (video is null)
        {
            return Result.NotFound("Video not found");
        }

        var (canAccess, _) = await CanAccessChannelAsync(video.ChannelId);
        if (!canAccess)
        {
            return Result.Forbidden();
        }

        var entries = await playlistVideoRepository.FindAsync(new SearchOptions<PlaylistVideo>
        {
            Query = x => x.VideoId == videoId
        }, x => x.PlaylistId);

        return Result.Success(new GetVideoPlaylistIdsResponse(entries.ToList()));
    }

    public async Task<Result<ThumbnailFileInfo>> GetVideoThumbnailAsync(int videoId)
    {
        var video = await videoRepository.FindOneAsync(videoId);
        if (video is null || string.IsNullOrEmpty(video.FilePath))
        {
            return Result.NotFound("Video or file path not found");
        }

        string dir = Path.GetDirectoryName(video.FilePath)!;
        string stem = Path.GetFileNameWithoutExtension(video.FilePath);
        var info = ResolveThumbnail(dir, stem);
        return info is null ? Result.NotFound("Thumbnail not found") : Result.Success(info);
    }

    public async Task<Result> UpdateChannelAsync(int channelId, UpdateCustomChannelRequest request)
    {
        try
        {
            var (canAccess, channel) = await CanAccessChannelAsync(channelId);
            if (!canAccess)
            {
                return Result.Forbidden();
            }

            if (channel is null || channel.Platform != "Custom")
            {
                return Result.NotFound("Channel not found");
            }

            channel.Name = request.Name;
            channel.Description = request.Description;
            channel.ThumbnailUrl = request.ThumbnailUrl;
            await channelRepository.UpdateAsync(channel);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating custom channel {ChannelId}", channelId);
            return Result.Error("An error occurred while updating the channel");
        }
    }

    public async Task<Result> UpdatePlaylistAsync(int playlistId, UpdateCustomChannelPlaylistRequest request)
    {
        var playlist = await playlistRepository.FindOneAsync(playlistId);
        if (playlist is null)
        {
            return Result.NotFound("Playlist not found");
        }

        var (canAccess, _) = await CanAccessChannelAsync(playlist.ChannelId);
        if (!canAccess)
        {
            return Result.Forbidden();
        }

        playlist.Name = request.Name;
        playlist.Description = request.Description;
        await playlistRepository.UpdateAsync(playlist);
        return Result.Success();
    }

    public async Task<Result> UpdateVideoAsync(int videoId, UpdateCustomVideoRequest request)
    {
        try
        {
            var video = await videoRepository.FindOneAsync(new SearchOptions<Video>
            {
                Query = x => x.Id == videoId,
                Include = query => query.Include(x => x.Channel)
            });
            if (video is null)
            {
                return Result.NotFound("Video not found");
            }

            var (canAccess, _) = await CanAccessChannelAsync(video.ChannelId);
            if (!canAccess)
            {
                return Result.Forbidden();
            }

            video.Title = request.Title;
            video.Description = request.Description;
            video.ThumbnailUrl = request.ThumbnailUrl;
            video.UploadDate = request.UploadDate;
            video.Duration = request.Duration;

            if (!string.IsNullOrEmpty(request.FilePath))
            {
                video.FilePath = request.FilePath;
                if (File.Exists(request.FilePath))
                {
                    video.FileSize = new FileInfo(request.FilePath).Length;
                    video.DownloadedAt ??= DateTime.UtcNow;
                }
            }

            if (!string.IsNullOrEmpty(video.Title) &&
                !string.Equals(video.Title, video.VideoId, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(video.Title, Path.GetFileNameWithoutExtension(video.FilePath ?? ""), StringComparison.OrdinalIgnoreCase))
            {
                video.NeedsMetadataReview = false;
            }

            await videoRepository.UpdateAsync(video);

            if (request.PlaylistIds is not null)
            {
                await SyncVideoPlaylistsAsync(video.Id, video.ChannelId, request.PlaylistIds);
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating video {VideoId}", videoId);
            return Result.Error("An error occurred while updating the video");
        }
    }

    public async Task<Result<string>> UploadChannelThumbnailAsync(int channelId, Stream fileStream, string fileName)
    {
        var (canAccess, channel) = await CanAccessChannelAsync(channelId);
        if (!canAccess)
        {
            return Result.Forbidden();
        }

        if (channel is null || channel.Platform != "Custom")
        {
            return Result.NotFound("Channel not found");
        }

        string dir = GetCustomChannelThumbnailDirectory(channel);
        Directory.CreateDirectory(dir);
        string ext = NormaliseImageExtension(Path.GetExtension(fileName));
        string thumbPath = Path.Combine(dir, "channel" + ext);
        await using (var fs = File.Create(thumbPath))
        {
            await fileStream.CopyToAsync(fs);
        }

        foreach (string other in AllowedImageExtensions)
        {
            if (other == ext)
            {
                continue;
            }

            string otherPath = Path.Combine(dir, "channel" + other);
            if (File.Exists(otherPath))
            {
                File.Delete(otherPath);
                break;
            }
        }

        channel.ThumbnailUrl = $"/api/custom/channels/{channelId}/thumbnail";
        await channelRepository.UpdateAsync(channel);
        return Result.Success(channel.ThumbnailUrl);
    }

    public async Task<Result<string>> UploadPlaylistThumbnailAsync(int playlistId, Stream fileStream, string fileName)
    {
        var playlist = await playlistRepository.FindOneAsync(new SearchOptions<Playlist>
        {
            Query = x => x.Id == playlistId,
            Include = query => query.Include(x => x.Channel)
        });
        if (playlist is null)
        {
            return Result.NotFound("Playlist not found");
        }

        var (canAccess, _) = await CanAccessChannelAsync(playlist.ChannelId);
        if (!canAccess)
        {
            return Result.Forbidden();
        }

        string dir = GetPlaylistThumbnailDirectory(playlist);
        Directory.CreateDirectory(dir);
        string ext = NormaliseImageExtension(Path.GetExtension(fileName));
        string thumbPath = Path.Combine(dir, playlist.PlaylistId + ext);
        await using (var fs = File.Create(thumbPath))
        {
            await fileStream.CopyToAsync(fs);
        }

        playlist.ThumbnailUrl = $"/api/custom/playlists/{playlistId}/thumbnail";
        await playlistRepository.UpdateAsync(playlist);
        return Result.Success(playlist.ThumbnailUrl);
    }

    public async Task<Result<string>> UploadVideoThumbnailAsync(int videoId, Stream fileStream, string fileName)
    {
        var video = await videoRepository.FindOneAsync(videoId);
        if (video is null)
        {
            return Result.NotFound("Video not found");
        }

        var (canAccess, _) = await CanAccessChannelAsync(video.ChannelId);
        if (!canAccess)
        {
            return Result.Forbidden();
        }

        if (string.IsNullOrEmpty(video.FilePath))
        {
            return Result.Invalid([new ValidationError("FilePath", "Please link a file path to this video first.")]);
        }

        string dir = Path.GetDirectoryName(video.FilePath)!;
        string stem = Path.GetFileNameWithoutExtension(video.FilePath);
        string ext = NormaliseImageExtension(Path.GetExtension(fileName));
        string thumbPath = Path.Combine(dir, stem + ext);
        await using (var fs = File.Create(thumbPath))
        {
            await fileStream.CopyToAsync(fs);
        }

        video.ThumbnailUrl = $"/api/custom/videos/{videoId}/thumbnail";
        await videoRepository.UpdateAsync(video);
        return Result.Success(video.ThumbnailUrl);
    }

    private static string NormaliseImageExtension(string ext)
    {
        ext = ext.ToLowerInvariant();
        return AllowedImageExtensions.Contains(ext) ? ext : ".jpg";
    }

    private static ThumbnailFileInfo? ResolveThumbnail(string directory, string stem)
    {
        foreach (string ext in AllowedImageExtensions)
        {
            string path = Path.Combine(directory, stem + ext);
            if (File.Exists(path))
            {
                string contentType = ext == ".png" ? "image/png" : ext == ".webp" ? "image/webp" : "image/jpeg";
                return new ThumbnailFileInfo(path, contentType);
            }
        }
        return null;
    }

    private async Task<(bool CanAccess, Channel? Channel)> CanAccessChannelAsync(int channelId)
    {
        var channel = await channelRepository.FindOneAsync(channelId);
        if (userContextService.IsAdministrator())
        {
            return (true, channel);
        }

        string? userId = userContextService.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return (false, channel);
        }

        var access = await userChannelRepository.FindOneAsync(new SearchOptions<UserChannel>
        {
            Query = x => x.UserId == userId && x.ChannelId == channelId
        });
        return (access is not null, channel);
    }

    private string GetCustomChannelThumbnailDirectory(Channel channel) =>
        Path.Combine(GetDownloadPath(), "_Custom", channel.ChannelId);

    private string GetDownloadPath() =>
            configuration.GetValue<string>("VideoDownload:OutputPath")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

    private string GetPlaylistThumbnailDirectory(Playlist playlist) =>
        Path.Combine(GetDownloadPath(), playlist.Channel.ChannelId, "Playlists");

    private async Task SyncVideoPlaylistsAsync(int videoId, int channelId, IReadOnlyList<int> desiredPlaylistIds)
    {
        var channelPlaylistIds = (await playlistRepository.FindAsync(new SearchOptions<Playlist>
        {
            Query = x => x.ChannelId == channelId
        }, x => x.Id)).ToHashSet();

        var validDesiredIds = desiredPlaylistIds.Where(channelPlaylistIds.Contains).ToHashSet();

        var existing = await playlistVideoRepository.FindAsync(new SearchOptions<PlaylistVideo>
        {
            Query = x => x.VideoId == videoId && channelPlaylistIds.Contains(x.PlaylistId)
        });

        var existingIds = existing.Select(x => x.PlaylistId).ToHashSet();

        var toRemove = existing.Where(x => !validDesiredIds.Contains(x.PlaylistId)).ToList();
        if (toRemove.Count > 0)
        {
            await playlistVideoRepository.DeleteAsync(toRemove);
        }

        foreach (int playlistId in validDesiredIds.Except(existingIds))
        {
            int maxOrder = (await playlistVideoRepository.FindAsync(new SearchOptions<PlaylistVideo>
            {
                Query = x => x.PlaylistId == playlistId
            }, x => (int?)x.Order)).Max() ?? -1;

            await playlistVideoRepository.InsertAsync(new PlaylistVideo
            {
                PlaylistId = playlistId,
                VideoId = videoId,
                Order = maxOrder + 1
            });
        }
    }

    private async Task TryDeletePlaylistThumbnailFileAsync(Playlist playlist)
    {
        try
        {
            if (playlist.ThumbnailUrl?.StartsWith("/api/custom/playlists/") != true)
            {
                return;
            }

            var playlistWithChannel = await playlistRepository.FindOneAsync(new SearchOptions<Playlist>
            {
                Query = x => x.Id == playlist.Id,
                Include = query => query.Include(x => x.Channel)
            });
            if (playlistWithChannel?.Channel is null)
            {
                return;
            }

            string dir = GetPlaylistThumbnailDirectory(playlistWithChannel);
            foreach (string ext in AllowedImageExtensions)
            {
                string path = Path.Combine(dir, playlist.PlaylistId + ext);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete thumbnail file for playlist {PlaylistId}", playlist.Id);
        }
    }
}