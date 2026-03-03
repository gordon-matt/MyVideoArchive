using Ardalis.Result;
using Hangfire;
using MyVideoArchive.Models.Api;
using MyVideoArchive.Models.Metadata;

namespace MyVideoArchive.Services;

public class CustomPlaylistService : ICustomPlaylistService
{
    private static readonly string[] AllowedImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    private readonly ILogger<CustomPlaylistService> logger;
    private readonly IConfiguration configuration;
    private readonly IUserContextService userContextService;
    private readonly IBackgroundJobClient backgroundJobClient;
    private readonly VideoMetadataProviderFactory metadataProviderFactory;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<CustomPlaylist> customPlaylistRepository;
    private readonly IRepository<CustomPlaylistVideo> customPlaylistVideoRepository;
    private readonly IRepository<Tag> tagRepository;
    private readonly IRepository<UserChannel> userChannelRepository;
    private readonly IRepository<Video> videoRepository;
    private readonly IRepository<VideoTag> videoTagRepository;

    public CustomPlaylistService(
        ILogger<CustomPlaylistService> logger,
        IConfiguration configuration,
        IUserContextService userContextService,
        IBackgroundJobClient backgroundJobClient,
        VideoMetadataProviderFactory metadataProviderFactory,
        IHttpClientFactory httpClientFactory,
        IRepository<Channel> channelRepository,
        IRepository<CustomPlaylist> customPlaylistRepository,
        IRepository<CustomPlaylistVideo> customPlaylistVideoRepository,
        IRepository<Tag> tagRepository,
        IRepository<UserChannel> userChannelRepository,
        IRepository<Video> videoRepository,
        IRepository<VideoTag> videoTagRepository)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.userContextService = userContextService;
        this.backgroundJobClient = backgroundJobClient;
        this.metadataProviderFactory = metadataProviderFactory;
        this.httpClientFactory = httpClientFactory;
        this.channelRepository = channelRepository;
        this.customPlaylistRepository = customPlaylistRepository;
        this.customPlaylistVideoRepository = customPlaylistVideoRepository;
        this.tagRepository = tagRepository;
        this.userChannelRepository = userChannelRepository;
        this.videoRepository = videoRepository;
        this.videoTagRepository = videoTagRepository;
    }

    public async Task<Result> AddVideoToPlaylistAsync(int id, int videoId)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
                return Result.Unauthorized();

            var playlist = await customPlaylistRepository.FindOneAsync(id);
            if (playlist is null)
                return Result.NotFound("Playlist not found");
            if (playlist.UserId != userId)
                return Result.Forbidden();

            bool videoExists = await videoRepository.ExistsAsync(x => x.Id == videoId);
            if (!videoExists)
                return Result.NotFound("Video not found");

            bool alreadyInPlaylist = await customPlaylistVideoRepository.ExistsAsync(
                x => x.CustomPlaylistId == id && x.VideoId == videoId);
            if (alreadyInPlaylist)
                return Result.Success();

            var existingOrders = (await customPlaylistVideoRepository.FindAsync(
                new SearchOptions<CustomPlaylistVideo> { Query = x => x.CustomPlaylistId == id },
                x => (int?)x.Order)).ToList();
            int maxOrder = existingOrders.Count > 0 ? existingOrders.Max() ?? -1 : -1;

            await customPlaylistVideoRepository.InsertAsync(new CustomPlaylistVideo
            {
                CustomPlaylistId = id,
                VideoId = videoId,
                Order = maxOrder + 1
            });

            return Result.Success();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(ex, "Error adding video {VideoId} to playlist {PlaylistId}", videoId, id);
            return Result.Error("An error occurred while adding the video");
        }
    }

    public async Task<Result<CreatePlaylistResponse>> CreatePlaylistAsync(CreateCustomPlaylistRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
                return Result.Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Name))
                return Result.Invalid([new ValidationError("Name", "Playlist name is required")]);

            var playlist = new CustomPlaylist
            {
                UserId = userId,
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            await customPlaylistRepository.InsertAsync(playlist);
            return Result.Success(new CreatePlaylistResponse(playlist.Id, playlist.Name));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(ex, "Error creating custom playlist");
            return Result.Error("An error occurred while creating the playlist");
        }
    }

    public async Task<Result<PreviewPlaylistResponse>> PreviewPlaylistAsync(PreviewPlaylistRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
                return Result.Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Url))
                return Result.Invalid([new ValidationError("Url", "A playlist URL is required")]);

            var provider = metadataProviderFactory.GetProvider(request.Url);
            if (provider is null)
                return Result.Invalid([new ValidationError("Url", "No metadata provider found for this URL")]);

            var playlistMeta = await provider.GetPlaylistMetadataAsync(request.Url, cancellationToken);
            if (playlistMeta is null)
                return Result.Invalid([new ValidationError("Url", "Could not retrieve playlist metadata. Please check the URL and try again.")]);

            var videoEntries = await provider.GetPlaylistVideosAsync(request.Url, cancellationToken);
            if (videoEntries.Count == 0)
                return Result.Invalid([new ValidationError("Url", "The playlist appears to be empty or could not be read.")]);

            var videoIds = videoEntries
                .Where(v => !string.IsNullOrEmpty(v.VideoId))
                .Select(v => v.VideoId)
                .ToList();

            var existingVideos = await videoRepository.FindAsync(
                new SearchOptions<Video>
                {
                    Query = x => videoIds.Contains(x.VideoId) && x.Platform == playlistMeta.Platform
                },
                x => x.VideoId);

            var inLibrarySet = existingVideos.ToHashSet();

            var videos = videoEntries
                .Where(v => !string.IsNullOrEmpty(v.VideoId))
                .Select(v => new PreviewVideoItem(
                    v.VideoId,
                    v.Title,
                    v.ThumbnailUrl,
                    v.Duration.HasValue ? (int?)v.Duration.Value.TotalSeconds : null,
                    v.ChannelName,
                    v.Url,
                    inLibrarySet.Contains(v.VideoId)))
                .ToList();

            return Result.Success(new PreviewPlaylistResponse(
                playlistMeta.Name,
                playlistMeta.Description,
                playlistMeta.ThumbnailUrl,
                playlistMeta.Platform,
                videos));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(ex, "Error previewing playlist from URL {Url}", request.Url);
            return Result.Error("An error occurred while fetching the playlist");
        }
    }

    public async Task<Result<ClonePlaylistResponse>> ClonePlaylistAsync(ClonePlaylistRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
                return Result.Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Url))
                return Result.Invalid([new ValidationError("Url", "A playlist URL is required")]);

            if (request.SelectedVideoIds.Count == 0)
                return Result.Invalid([new ValidationError("SelectedVideoIds", "Please select at least one video to clone")]);

            var provider = metadataProviderFactory.GetProvider(request.Url);
            if (provider is null)
                return Result.Invalid([new ValidationError("Url", "No metadata provider found for this URL")]);

            var playlistMeta = await provider.GetPlaylistMetadataAsync(request.Url, cancellationToken);
            if (playlistMeta is null)
                return Result.Invalid([new ValidationError("Url", "Could not retrieve playlist metadata. Please check the URL and try again.")]);

            var videoEntries = await provider.GetPlaylistVideosAsync(request.Url, cancellationToken);
            var selectedSet = request.SelectedVideoIds.ToHashSet();
            var selectedEntries = videoEntries
                .Where(v => !string.IsNullOrEmpty(v.VideoId) && selectedSet.Contains(v.VideoId))
                .ToList();

            if (selectedEntries.Count == 0)
                return Result.Invalid([new ValidationError("SelectedVideoIds", "None of the selected videos could be found in the playlist.")]);

            var playlist = new CustomPlaylist
            {
                UserId = userId,
                Name = playlistMeta.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(playlistMeta.Description) ? null : playlistMeta.Description.Trim(),
                CreatedAt = DateTime.UtcNow
            };
            await customPlaylistRepository.InsertAsync(playlist);

            if (!string.IsNullOrEmpty(playlistMeta.ThumbnailUrl))
            {
                try
                {
                    using var http = httpClientFactory.CreateClient();
                    var imageBytes = await http.GetByteArrayAsync(playlistMeta.ThumbnailUrl, cancellationToken);
                    string uploadDir = GetCustomPlaylistsThumbnailDirectory();
                    Directory.CreateDirectory(uploadDir);
                    string thumbPath = Path.Combine(uploadDir, $"{playlist.Id}-thumbnail.jpg");
                    await File.WriteAllBytesAsync(thumbPath, imageBytes, cancellationToken);
                    playlist.ThumbnailUrl = $"/api/custom-playlists/{playlist.Id}/thumbnail";
                    await customPlaylistRepository.UpdateAsync(playlist);
                }
                catch (Exception ex)
                {
                    if (logger.IsEnabled(LogLevel.Warning))
                        logger.LogWarning(ex, "Failed to download thumbnail for cloned playlist {PlaylistId}", playlist.Id);
                }
            }

            int newVideoCount = 0;
            int alreadyInLibraryCount = 0;
            int order = 0;

            foreach (var videoMeta in selectedEntries)
            {
                string channelPlatformId = videoMeta.ChannelId ?? videoMeta.ChannelName ?? "unknown";
                var channel = await channelRepository.FindOneAsync(new SearchOptions<Channel>
                {
                    Query = x => x.ChannelId == channelPlatformId && x.Platform == videoMeta.Platform
                });

                if (channel is null)
                {
                    ChannelMetadata? channelMeta = null;
                    if (!string.IsNullOrEmpty(videoMeta.ChannelId))
                    {
                        var channelUrl = $"https://www.youtube.com/channel/{videoMeta.ChannelId}";
                        channelMeta = await provider.GetChannelMetadataAsync(channelUrl, cancellationToken);
                    }

                    channel = new Channel
                    {
                        ChannelId = channelPlatformId,
                        Name = videoMeta.ChannelName ?? "Unknown Channel",
                        Url = string.IsNullOrEmpty(videoMeta.ChannelId)
                            ? string.Empty
                            : $"https://www.youtube.com/channel/{videoMeta.ChannelId}",
                        Platform = videoMeta.Platform,
                        SubscribedAt = DateTime.UtcNow
                    };
                    await channelRepository.InsertAsync(channel);
                }

                var video = await videoRepository.FindOneAsync(new SearchOptions<Video>
                {
                    Query = x => x.VideoId == videoMeta.VideoId && x.Platform == videoMeta.Platform
                });

                if (video is null)
                {
                    video = new Video
                    {
                        VideoId = videoMeta.VideoId,
                        Title = videoMeta.Title,
                        Description = videoMeta.Description,
                        Url = videoMeta.Url,
                        ThumbnailUrl = videoMeta.ThumbnailUrl,
                        Platform = videoMeta.Platform,
                        Duration = videoMeta.Duration,
                        UploadDate = videoMeta.UploadDate,
                        ViewCount = videoMeta.ViewCount,
                        LikeCount = videoMeta.LikeCount,
                        ChannelId = channel.Id,
                        IsQueued = true
                    };
                    await videoRepository.InsertAsync(video);
                    backgroundJobClient.Enqueue<VideoDownloadJob>(job => job.ExecuteAsync(video.Id, CancellationToken.None));
                    newVideoCount++;
                }
                else
                {
                    alreadyInLibraryCount++;
                }

                bool tagIt = !await userChannelRepository.ExistsAsync(x => x.Channel!.ChannelId == channelPlatformId && x.UserId == userId);
                if (tagIt)
                {
                    var standaloneTag = await GetOrCreateTagAsync(userId, Constants.StandaloneTag);
                    var alreadyTagged = await videoTagRepository.ExistsAsync(x => x.VideoId == video.Id && x.TagId == standaloneTag.Id);
                    if (!alreadyTagged)
                    {
                        await videoTagRepository.InsertAsync(new VideoTag
                        {
                            VideoId = video.Id,
                            TagId = standaloneTag.Id
                        });
                    }
                }

                bool alreadyInPlaylist = await customPlaylistVideoRepository.ExistsAsync(
                    x => x.CustomPlaylistId == playlist.Id && x.VideoId == video.Id);

                if (!alreadyInPlaylist)
                {
                    await customPlaylistVideoRepository.InsertAsync(new CustomPlaylistVideo
                    {
                        CustomPlaylistId = playlist.Id,
                        VideoId = video.Id,
                        Order = order++
                    });
                }
            }

            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation(
                    "Cloned playlist '{Name}' with {Total} videos ({New} new, {Existing} already in library)",
                    playlist.Name, order, newVideoCount, alreadyInLibraryCount);

            return Result.Success(new ClonePlaylistResponse(
                playlist.Id,
                playlist.Name,
                order,
                newVideoCount,
                alreadyInLibraryCount));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(ex, "Error cloning playlist from URL {Url}", request.Url);
            return Result.Error("An error occurred while cloning the playlist");
        }
    }

    public async Task<Result> DeletePlaylistAsync(int id)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
                return Result.Unauthorized();

            var playlist = await customPlaylistRepository.FindOneAsync(id);
            if (playlist is null)
                return Result.NotFound("Playlist not found");
            if (playlist.UserId != userId)
                return Result.Forbidden();

            await customPlaylistRepository.DeleteAsync(playlist);
            return Result.Success();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(ex, "Error deleting custom playlist {PlaylistId}", id);
            return Result.Error("An error occurred while deleting the playlist");
        }
    }

    public async Task<Result<GetPlaylistsResponse>> GetPlaylistsAsync(int page = 1, int pageSize = 60)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
                return Result.Unauthorized();

            var pagedPlaylists = await customPlaylistRepository.FindAsync(
                new SearchOptions<CustomPlaylist>
                {
                    Query = x => x.UserId == userId,
                    OrderBy = q => q.OrderByDescending(x => x.CreatedAt),
                    PageNumber = page,
                    PageSize = pageSize,
                    Include = q => q.Include(x => x.CustomPlaylistVideos)
                });

            var playlists = pagedPlaylists.Select(x => new CustomPlaylistSummary(
                x.Id,
                x.Name,
                x.Description,
                x.ThumbnailUrl,
                x.CreatedAt,
                x.CustomPlaylistVideos.Count)).ToList();

            return Result.Success(new GetPlaylistsResponse(
                playlists,
                page,
                pageSize,
                pagedPlaylists.ItemCount,
                pagedPlaylists.PageCount));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(ex, "Error retrieving custom playlists");
            return Result.Error("An error occurred while retrieving playlists");
        }
    }

    public async Task<Result<GetPlaylistsForVideoResponse>> GetPlaylistsForVideoAsync(int videoId)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
                return Result.Unauthorized();

            var memberships = await customPlaylistVideoRepository.FindAsync(
                new SearchOptions<CustomPlaylistVideo>
                {
                    Query = x => x.VideoId == videoId && x.CustomPlaylist!.UserId == userId,
                    Include = q => q.Include(x => x.CustomPlaylist)
                });

            var playlists = memberships
                .Select(x => new PlaylistSummaryItem(x.CustomPlaylist!.Id, x.CustomPlaylist.Name))
                .ToList();

            return Result.Success(new GetPlaylistsForVideoResponse(playlists));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(ex, "Error retrieving playlists for video {VideoId}", videoId);
            return Result.Error("An error occurred while retrieving playlists for video");
        }
    }

    public Task<Result<ThumbnailFileInfo>> GetPlaylistThumbnailAsync(int id)
    {
        string uploadDir = GetCustomPlaylistsThumbnailDirectory();

        foreach (string ext in AllowedImageExtensions)
        {
            string thumbPath = Path.Combine(uploadDir, $"{id}-thumbnail{ext}");
            if (File.Exists(thumbPath))
            {
                string contentType = ext switch
                {
                    ".png" => "image/png",
                    ".webp" => "image/webp",
                    _ => "image/jpeg"
                };
                return Task.FromResult(Result.Success(new ThumbnailFileInfo(thumbPath, contentType)));
            }
        }

        return Task.FromResult(Result<ThumbnailFileInfo>.NotFound());
    }

    public async Task<Result<GetPlaylistVideosResponse>> GetPlaylistVideosAsync(int id, int page = 1, int pageSize = 60)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
                return Result.Unauthorized();

            var playlist = await customPlaylistRepository.FindOneAsync(id);
            if (playlist is null)
                return Result.NotFound("Playlist not found");
            if (playlist.UserId != userId)
                return Result.Forbidden();

            var pagedPlaylistVideos = await customPlaylistVideoRepository.FindAsync(
                new SearchOptions<CustomPlaylistVideo>
                {
                    Query = x => x.CustomPlaylistId == id,
                    OrderBy = q => q.OrderBy(x => x.Order),
                    PageNumber = page,
                    PageSize = pageSize,
                    Include = q => q.Include(x => x.Video).ThenInclude(v => v.Channel!)
                });

            var videos = pagedPlaylistVideos.Select(x => new PlaylistVideoEntry(
                x.Order,
                new PlaylistVideoDetail(
                    x.Video.Id,
                    x.Video.Title,
                    x.Video.ThumbnailUrl,
                    x.Video.Duration,
                    x.Video.DownloadedAt,
                    x.Video.Platform,
                    x.Video.Url,
                    x.Video.ViewCount,
                    x.Video.LikeCount,
                    x.Video.UploadDate,
                    x.Video.Description,
                    new ChannelInfo(x.Video.Channel.Id, x.Video.Channel.Name)))).ToList();

            return Result.Success(new GetPlaylistVideosResponse(
                new PlaylistInfo(playlist.Id, playlist.Name, playlist.Description, playlist.ThumbnailUrl),
                videos,
                page,
                pageSize,
                pagedPlaylistVideos.ItemCount,
                pagedPlaylistVideos.PageCount));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(ex, "Error retrieving videos for custom playlist {PlaylistId}", id);
            return Result.Error("An error occurred while retrieving playlist videos");
        }
    }

    public async Task<Result> RemoveVideoFromPlaylistAsync(int id, int videoId)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
                return Result.Unauthorized();

            var playlist = await customPlaylistRepository.FindOneAsync(id);
            if (playlist is null)
                return Result.NotFound("Playlist not found");
            if (playlist.UserId != userId)
                return Result.Forbidden();

            var entry = await customPlaylistVideoRepository.FindOneAsync(new SearchOptions<CustomPlaylistVideo>
            {
                Query = x => x.CustomPlaylistId == id && x.VideoId == videoId
            });

            if (entry is null)
                return Result.NotFound("Video not found in playlist");

            await customPlaylistVideoRepository.DeleteAsync(entry);
            return Result.Success();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(ex, "Error removing video {VideoId} from playlist {PlaylistId}", videoId, id);
            return Result.Error("An error occurred while removing the video");
        }
    }

    public async Task<Result<UpdatePlaylistResponse>> UpdatePlaylistAsync(int id, CreateCustomPlaylistRequest request)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
                return Result.Unauthorized();

            var playlist = await customPlaylistRepository.FindOneAsync(id);
            if (playlist is null)
                return Result.NotFound("Playlist not found");
            if (playlist.UserId != userId)
                return Result.Forbidden();

            playlist.Name = request.Name?.Trim() ?? playlist.Name;
            playlist.Description = request.Description?.Trim();
            await customPlaylistRepository.UpdateAsync(playlist);

            return Result.Success(new UpdatePlaylistResponse(playlist.Id, playlist.Name));
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(ex, "Error updating custom playlist {PlaylistId}", id);
            return Result.Error("An error occurred while updating the playlist");
        }
    }

    public async Task<Result<string>> UploadThumbnailAsync(int id, Stream fileStream, string fileName)
    {
        try
        {
            string? userId = userContextService.GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
                return Result.Unauthorized();

            var playlist = await customPlaylistRepository.FindOneAsync(id);
            if (playlist is null)
                return Result.NotFound("Playlist not found");
            if (playlist.UserId != userId)
                return Result.Forbidden();

            string ext = NormaliseImageExtension(Path.GetExtension(fileName));
            string uploadDir = GetCustomPlaylistsThumbnailDirectory();
            Directory.CreateDirectory(uploadDir);

            foreach (string other in AllowedImageExtensions)
            {
                string otherPath = Path.Combine(uploadDir, $"{id}-thumbnail{other}");
                if (File.Exists(otherPath))
                    File.Delete(otherPath);
            }

            string thumbPath = Path.Combine(uploadDir, $"{id}-thumbnail{ext}");
            await using (var dest = File.Create(thumbPath))
            {
                await fileStream.CopyToAsync(dest);
            }

            playlist.ThumbnailUrl = $"/api/custom-playlists/{id}/thumbnail";
            await customPlaylistRepository.UpdateAsync(playlist);

            return Result.Success(playlist.ThumbnailUrl);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(ex, "Error uploading thumbnail for custom playlist {PlaylistId}", id);
            return Result.Error("An error occurred while saving the thumbnail");
        }
    }

    private string GetDownloadPath() =>
        configuration.GetValue<string>("VideoDownload:OutputPath")
        ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

    private string GetCustomPlaylistsThumbnailDirectory() =>
        Path.Combine(GetDownloadPath(), "_CustomPlaylists");

    private static string NormaliseImageExtension(string ext) =>
        ext.ToLowerInvariant() switch
        {
            ".jpeg" => ".jpg",
            ".jpg" or ".png" or ".webp" => ext.ToLowerInvariant(),
            _ => ".jpg"
        };

    private async Task<Tag> GetOrCreateTagAsync(string userId, string name)
    {
        var existing = await tagRepository.FindOneAsync(new SearchOptions<Tag>
        {
            Query = x => x.UserId == userId && x.Name.ToLower() == name.ToLower()
        });

        if (existing is not null)
            return existing;

        var tag = new Tag { UserId = userId, Name = name };
        await tagRepository.InsertAsync(tag);
        return tag;
    }
}