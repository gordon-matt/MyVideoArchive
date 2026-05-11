using Ardalis.Result;
using ResultStatus = Ardalis.Result.ResultStatus;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MyVideoArchive.Models.Requests.AdditionalContent;
using MyVideoArchive.Models.Responses;

namespace MyVideoArchive.Services;

public class AdditionalContentService : IAdditionalContentService
{
    private readonly ILogger<AdditionalContentService> logger;
    private readonly IConfiguration configuration;
    private readonly IRepository<AdditionalContentItem> repository;
    private readonly IRepository<Channel> channelRepository;
    private readonly IRepository<Playlist> playlistRepository;
    private readonly IRepository<PlaylistVideo> playlistVideoRepository;
    private readonly IRepository<Video> videoRepository;
    private readonly IRepository<PlaylistAdditionalContentItem> playlistLinkRepository;
    private readonly IRepository<VideoAdditionalContentItem> videoLinkRepository;

    public AdditionalContentService(
        ILogger<AdditionalContentService> logger,
        IConfiguration configuration,
        IRepository<AdditionalContentItem> repository,
        IRepository<Channel> channelRepository,
        IRepository<Playlist> playlistRepository,
        IRepository<PlaylistVideo> playlistVideoRepository,
        IRepository<Video> videoRepository,
        IRepository<PlaylistAdditionalContentItem> playlistLinkRepository,
        IRepository<VideoAdditionalContentItem> videoLinkRepository)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.repository = repository;
        this.channelRepository = channelRepository;
        this.playlistRepository = playlistRepository;
        this.playlistVideoRepository = playlistVideoRepository;
        this.videoRepository = videoRepository;
        this.playlistLinkRepository = playlistLinkRepository;
        this.videoLinkRepository = videoLinkRepository;
    }

    public async Task<Result<IReadOnlyList<AdditionalContentItemDto>>> GetChannelItemsAsync(int channelId)
    {
        var items = await repository.FindAsync(new SearchOptions<AdditionalContentItem>
        {
            Query = x => x.ChannelId == channelId,
            Include = query => query
                .Include(x => x.PlaylistLinks)
                .ThenInclude(l => l.Playlist)
        });

        return Result.Success<IReadOnlyList<AdditionalContentItemDto>>(
            items.Select(ToDto).ToList());
    }

    public async Task<Result<IReadOnlyList<AdditionalContentItemDto>>> GetItemsForVideoAsync(int videoId)
    {
        var items = await repository.FindAsync(new SearchOptions<AdditionalContentItem>
        {
            Query = x => x.VideoLinks.Any(v => v.VideoId == videoId),
            Include = query => query
                .Include(x => x.PlaylistLinks)
                .ThenInclude(l => l.Playlist)
        });

        return Result.Success<IReadOnlyList<AdditionalContentItemDto>>(items.Select(ToDto).ToList());
    }

    public async Task<Result<IReadOnlyList<AdditionalContentItemDto>>> GetAvailableItemsForVideoOnPlaylistAsync(int playlistId, int videoId)
    {
        var playlist = await playlistRepository.FindOneAsync(playlistId);
        if (playlist is null)
        {
            return Result.NotFound("Playlist not found");
        }

        var video = await videoRepository.FindOneAsync(videoId);
        if (video is null)
        {
            return Result.NotFound("Video not found");
        }

        if (video.ChannelId != playlist.ChannelId)
        {
            return Result.Invalid([new ValidationError(nameof(videoId), "Video does not belong to the same channel as the playlist.")]);
        }

        var inPlaylist = await playlistVideoRepository.FindOneAsync(new SearchOptions<PlaylistVideo>
        {
            Query = x => x.PlaylistId == playlistId && x.VideoId == videoId
        });
        if (inPlaylist is null)
        {
            return Result.Invalid([new ValidationError(nameof(videoId), "Video is not in this playlist.")]);
        }

        int channelId = playlist.ChannelId;

        var items = await repository.FindAsync(new SearchOptions<AdditionalContentItem>
        {
            Query = x => x.ChannelId == channelId &&
                (!x.PlaylistLinks.Any() || x.PlaylistLinks.Any(l => l.PlaylistId == playlistId)) &&
                !x.VideoLinks.Any(v => v.VideoId == videoId),
            Include = query => query
                .Include(x => x.PlaylistLinks)
                .ThenInclude(l => l.Playlist)
                .Include(x => x.VideoLinks)
        });

        return Result.Success<IReadOnlyList<AdditionalContentItemDto>>(items.Select(ToDto).ToList());
    }

    public async Task<Result<AdditionalContentItemDto>> UploadAsync(int channelId, IFormFile file, IReadOnlyList<int>? playlistIds)
    {
        try
        {
            var channel = await channelRepository.FindOneAsync(channelId);
            if (channel is null)
            {
                return Result.NotFound("Channel not found");
            }

            var distinctPlaylistIds = (playlistIds ?? []).Distinct().ToList();
            foreach (int pid in distinctPlaylistIds)
            {
                var pl = await playlistRepository.FindOneAsync(pid);
                if (pl is null || pl.ChannelId != channelId)
                {
                    return Result.Invalid([new ValidationError("playlistIds", "One or more playlists are invalid for this channel.")]);
                }
            }

            string extrasDir = GetExtrasDirectoryRoot(channel);
            Directory.CreateDirectory(extrasDir);

            string originalExt = Path.GetExtension(file.FileName);
            string storedName = Guid.NewGuid().ToString("N") + originalExt;
            string filePath = Path.Combine(extrasDir, storedName);

            await using (var fs = File.Create(filePath))
            {
                await file.CopyToAsync(fs);
            }

            var item = new AdditionalContentItem
            {
                FileName = file.FileName,
                FilePath = filePath,
                ContentType = ResolveContentType(file.ContentType, originalExt),
                FileSize = file.Length,
                UploadedAt = DateTime.UtcNow,
                ChannelId = channelId
            };

            await repository.InsertAsync(item);

            foreach (int pid in distinctPlaylistIds)
            {
                await playlistLinkRepository.InsertAsync(new PlaylistAdditionalContentItem
                {
                    PlaylistId = pid,
                    AdditionalContentItemId = item.Id
                });
            }

            var reloaded = await repository.FindOneAsync(new SearchOptions<AdditionalContentItem>
            {
                Query = x => x.Id == item.Id,
                Include = query => query
                    .Include(x => x.PlaylistLinks)
                    .ThenInclude(l => l.Playlist)
            });

            logger.LogInformation("Uploaded additional content {FileName} to channel {ChannelId}", item.FileName, channelId);

            return Result.Success(ToDto(reloaded!));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error uploading additional content for channel {ChannelId}", channelId);
            return Result.Error("An error occurred while uploading the file");
        }
    }

    public async Task<Result> UpdateAsync(int id, UpdateAdditionalContentRequest request)
    {
        try
        {
            var item = await repository.FindOneAsync(new SearchOptions<AdditionalContentItem>
            {
                Query = x => x.Id == id,
                Include = query => query.Include(x => x.Channel)
            });

            if (item is null)
            {
                return Result.NotFound("Item not found");
            }

            var distinctPlaylistIds = (request.PlaylistIds ?? []).Distinct().ToList();
            foreach (int pid in distinctPlaylistIds)
            {
                var pl = await playlistRepository.FindOneAsync(pid);
                if (pl is null || pl.ChannelId != item.ChannelId)
                {
                    return Result.Invalid([new ValidationError(nameof(request.PlaylistIds), "One or more playlists are invalid for this channel.")]);
                }
            }

            item.FileName = request.FileName;

            var existingLinks = await playlistLinkRepository.FindAsync(new SearchOptions<PlaylistAdditionalContentItem>
            {
                Query = x => x.AdditionalContentItemId == id
            });
            var desired = distinctPlaylistIds.ToHashSet();
            var existingIds = existingLinks.Select(l => l.PlaylistId).ToHashSet();

            var toRemove = existingLinks.Where(l => !desired.Contains(l.PlaylistId)).ToList();
            if (toRemove.Count > 0)
            {
                await playlistLinkRepository.DeleteAsync(toRemove);
            }

            foreach (int pid in desired.Except(existingIds))
            {
                await playlistLinkRepository.InsertAsync(new PlaylistAdditionalContentItem
                {
                    PlaylistId = pid,
                    AdditionalContentItemId = item.Id
                });
            }

            await repository.UpdateAsync(item);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating additional content item {Id}", id);
            return Result.Error("An error occurred while updating the item");
        }
    }

    public async Task<Result> DeleteAsync(int id)
    {
        try
        {
            var item = await repository.FindOneAsync(id);
            if (item is null)
            {
                return Result.NotFound("Item not found");
            }

            if (File.Exists(item.FilePath))
            {
                File.Delete(item.FilePath);
            }

            await repository.DeleteAsync(item);
            logger.LogInformation("Deleted additional content item {Id}", id);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting additional content item {Id}", id);
            return Result.Error("An error occurred while deleting the item");
        }
    }

    public async Task<Result<AdditionalContentDownloadInfo>> GetDownloadInfoAsync(int id)
    {
        var item = await repository.FindOneAsync(id);
        if (item is null || !File.Exists(item.FilePath))
        {
            return Result.NotFound("File not found");
        }

        string contentType = item.ContentType ?? "application/octet-stream";
        string displayName = item.FileName;
        string storedExt = Path.GetExtension(item.FilePath);
        if (!string.IsNullOrEmpty(storedExt) && !item.FileName.EndsWith(storedExt, StringComparison.OrdinalIgnoreCase))
        {
            displayName += storedExt;
        }

        return Result.Success(new AdditionalContentDownloadInfo(item.FilePath, contentType, displayName));
    }

    public async Task<Result> LinkItemsToVideoAsync(int videoId, int playlistId, LinkAdditionalContentToVideoRequest request)
    {
        var available = await GetAvailableItemsForVideoOnPlaylistAsync(playlistId, videoId);
        if (!available.IsSuccess)
        {
            return available.Status switch
            {
                ResultStatus.NotFound => Result.NotFound(),
                ResultStatus.Invalid => Result.Invalid(available.ValidationErrors),
                _ => Result.Error(string.Join("; ", available.Errors))
            };
        }

        var video = await videoRepository.FindOneAsync(videoId);
        if (video is null)
        {
            return Result.NotFound("Video not found");
        }

        int channelId = video.ChannelId;

        var playlistIdsContainingVideo = await GetPlaylistIdsContainingVideoOnChannelAsync(videoId, channelId);

        var allowedIds = available.Value.Select(i => i.Id).ToHashSet();
        foreach (int itemId in (request.ItemIds ?? []).Distinct())
        {
            if (!allowedIds.Contains(itemId))
            {
                return Result.Invalid([new ValidationError(nameof(request.ItemIds), $"Item {itemId} is not available to associate for this video.")]);
            }

            var exists = await videoLinkRepository.FindOneAsync(new SearchOptions<VideoAdditionalContentItem>
            {
                Query = x => x.VideoId == videoId && x.AdditionalContentItemId == itemId
            });
            if (exists is null)
            {
                await videoLinkRepository.InsertAsync(new VideoAdditionalContentItem
                {
                    VideoId = videoId,
                    AdditionalContentItemId = itemId
                });
            }

            await EnsurePlaylistLinksForItemAsync(itemId, playlistIdsContainingVideo);
        }

        return Result.Success();
    }

    /// <summary>
    /// Distinct playlist DB ids (same channel as the video) that contain this video via <see cref="PlaylistVideo"/>.
    /// </summary>
    private async Task<IReadOnlyList<int>> GetPlaylistIdsContainingVideoOnChannelAsync(int videoId, int channelId)
    {
        var memberships = await playlistVideoRepository.FindAsync(new SearchOptions<PlaylistVideo>
        {
            Query = x => x.VideoId == videoId,
            Include = query => query.Include(x => x.Playlist)
        });

        return memberships
            .Where(m => m.Playlist is not null && m.Playlist.ChannelId == channelId)
            .Select(m => m.PlaylistId)
            .Distinct()
            .ToList();
    }

    private async Task EnsurePlaylistLinksForItemAsync(int itemId, IReadOnlyList<int> playlistIds)
    {
        foreach (int playlistId in playlistIds)
        {
            var linkExists = await playlistLinkRepository.FindOneAsync(new SearchOptions<PlaylistAdditionalContentItem>
            {
                Query = x => x.PlaylistId == playlistId && x.AdditionalContentItemId == itemId
            });
            if (linkExists is not null)
            {
                continue;
            }

            await playlistLinkRepository.InsertAsync(new PlaylistAdditionalContentItem
            {
                PlaylistId = playlistId,
                AdditionalContentItemId = itemId
            });
        }
    }

    public async Task<Result> UnlinkItemFromVideoAsync(int videoId, int itemId)
    {
        var link = await videoLinkRepository.FindOneAsync(new SearchOptions<VideoAdditionalContentItem>
        {
            Query = x => x.VideoId == videoId && x.AdditionalContentItemId == itemId
        });
        if (link is null)
        {
            return Result.NotFound("Association not found");
        }

        await videoLinkRepository.DeleteAsync(link);
        return Result.Success();
    }

    public async Task ImportFileAsync(string filePath, int channelId, int? playlistId, CancellationToken cancellationToken = default)
    {
        var exists = await repository.FindAsync(new SearchOptions<AdditionalContentItem>
        {
            CancellationToken = cancellationToken,
            Query = x => x.FilePath == filePath
        });

        if (exists.Count > 0)
        {
            return;
        }

        var fileInfo = new FileInfo(filePath);
        string ext = fileInfo.Extension;

        var item = new AdditionalContentItem
        {
            FileName = fileInfo.Name,
            FilePath = filePath,
            ContentType = ResolveContentType(null, ext),
            FileSize = fileInfo.Length,
            UploadedAt = fileInfo.LastWriteTimeUtc,
            ChannelId = channelId
        };

        await repository.InsertAsync(item, ContextOptions.ForCancellationToken(cancellationToken));

        if (playlistId.HasValue)
        {
            await playlistLinkRepository.InsertAsync(new PlaylistAdditionalContentItem
            {
                PlaylistId = playlistId.Value,
                AdditionalContentItemId = item.Id
            }, ContextOptions.ForCancellationToken(cancellationToken));
        }

        logger.LogInformation("Imported additional content file {FilePath}", filePath);
    }

    private string GetExtrasDirectoryRoot(Channel channel)
    {
        string downloadPath = GetDownloadPath();
        string channelFolder = channel.Platform == "Custom"
            ? Path.Combine(downloadPath, "_Custom", channel.ChannelId)
            : Path.Combine(downloadPath, channel.ChannelId);

        return Path.Combine(channelFolder, "_extras");
    }

    private string GetDownloadPath() =>
        configuration.GetValue<string>("VideoDownload:OutputPath")
        ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

    private static AdditionalContentItemDto ToDto(AdditionalContentItem item)
    {
        var ordered = item.PlaylistLinks
            .Where(l => l.Playlist is not null)
            .Select(l => (l.PlaylistId, Name: l.Playlist!.Name))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AdditionalContentItemDto(
            item.Id,
            item.FileName,
            item.ContentType,
            item.FileSize,
            item.UploadedAt,
            item.ChannelId,
            ordered.Select(t => t.PlaylistId).ToList(),
            ordered.Select(t => t.Name).ToList());
    }

    private static string ResolveContentType(string? providedType, string extension)
    {
        if (!string.IsNullOrWhiteSpace(providedType) && providedType != "application/octet-stream")
        {
            return providedType;
        }

        return extension.ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".mp3" => "audio/mpeg",
            ".zip" => "application/zip",
            ".rar" => "application/vnd.rar",
            ".7z" => "application/x-7z-compressed",
            ".txt" => "text/plain",
            ".srt" => "text/plain",
            ".vtt" => "text/vtt",
            ".epub" => "application/epub+zip",
            _ => "application/octet-stream"
        };
    }
}
