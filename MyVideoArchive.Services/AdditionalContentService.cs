using Ardalis.Result;
using Microsoft.AspNetCore.Http;
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

    public AdditionalContentService(
        ILogger<AdditionalContentService> logger,
        IConfiguration configuration,
        IRepository<AdditionalContentItem> repository,
        IRepository<Channel> channelRepository,
        IRepository<Playlist> playlistRepository)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.repository = repository;
        this.channelRepository = channelRepository;
        this.playlistRepository = playlistRepository;
    }

    public async Task<Result<IReadOnlyList<AdditionalContentItemDto>>> GetChannelItemsAsync(int channelId)
    {
        var items = await repository.FindAsync(new SearchOptions<AdditionalContentItem>
        {
            Query = x => x.ChannelId == channelId,
            Include = query => query
                .Include(x => x.Playlist)
                .Include(x => x.Video)
        });

        return Result.Success<IReadOnlyList<AdditionalContentItemDto>>(
            items.Select(ToDto).ToList());
    }

    public async Task<Result<IReadOnlyList<AdditionalContentItemDto>>> GetPlaylistItemsAsync(int playlistId)
    {
        var items = await repository.FindAsync(new SearchOptions<AdditionalContentItem>
        {
            Query = x => x.PlaylistId == playlistId,
            Include = query => query
                .Include(x => x.Playlist)
                .Include(x => x.Video)
        });

        return Result.Success<IReadOnlyList<AdditionalContentItemDto>>(
            items.Select(ToDto).ToList());
    }

    public async Task<Result<AdditionalContentItemDto>> UploadAsync(int channelId, IFormFile file, int? playlistId)
    {
        try
        {
            var channel = await channelRepository.FindOneAsync(channelId);
            if (channel is null)
            {
                return Result.NotFound("Channel not found");
            }

            Playlist? playlist = null;
            if (playlistId.HasValue)
            {
                playlist = await playlistRepository.FindOneAsync(playlistId.Value);
                if (playlist is null || playlist.ChannelId != channelId)
                {
                    return Result.Invalid([new ValidationError("PlaylistId", "Playlist not found or does not belong to this channel.")]);
                }
            }

            string extrasDir = GetExtrasDirectory(channel, playlist);
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
                ChannelId = channelId,
                PlaylistId = playlistId,
                VideoId = null
            };

            await repository.InsertAsync(item);
            logger.LogInformation("Uploaded additional content {FileName} to channel {ChannelId}", item.FileName, channelId);

            var dto = ToDto(item) with
            {
                PlaylistName = playlist?.Name
            };

            return Result.Success(dto);
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

            // If playlist changed, move the file to the new location
            if (item.PlaylistId != request.PlaylistId)
            {
                Playlist? newPlaylist = null;
                if (request.PlaylistId.HasValue)
                {
                    newPlaylist = await playlistRepository.FindOneAsync(request.PlaylistId.Value);
                    if (newPlaylist is null || newPlaylist.ChannelId != item.ChannelId)
                    {
                        return Result.Invalid([new ValidationError("PlaylistId", "Playlist not found or does not belong to this channel.")]);
                    }
                }

                string newDir = GetExtrasDirectory(item.Channel, newPlaylist);
                Directory.CreateDirectory(newDir);

                string newFilePath = Path.Combine(newDir, Path.GetFileName(item.FilePath));

                if (File.Exists(item.FilePath))
                {
                    File.Move(item.FilePath, newFilePath, overwrite: true);
                }

                item.FilePath = newFilePath;
                item.PlaylistId = request.PlaylistId;
            }

            item.FileName = request.FileName;
            item.VideoId = request.VideoId;

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
        // Append the original extension to the display name to ensure the browser
        // gets the correct file type, even if the user renamed without extension.
        string displayName = item.FileName;
        string storedExt = Path.GetExtension(item.FilePath);
        if (!string.IsNullOrEmpty(storedExt) && !item.FileName.EndsWith(storedExt, StringComparison.OrdinalIgnoreCase))
        {
            displayName += storedExt;
        }

        return Result.Success(new AdditionalContentDownloadInfo(item.FilePath, contentType, displayName));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Imports an existing file from disk into the database without copying it.
    /// Used by the file system scan job.
    /// </summary>
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
            ChannelId = channelId,
            PlaylistId = playlistId,
            VideoId = null
        };

        await repository.InsertAsync(item, ContextOptions.ForCancellationToken(cancellationToken));
        logger.LogInformation("Imported additional content file {FilePath}", filePath);
    }

    private string GetExtrasDirectory(Channel channel, Playlist? playlist)
    {
        string downloadPath = GetDownloadPath();
        string channelFolder = channel.Platform == "Custom"
            ? Path.Combine(downloadPath, "_Custom", channel.ChannelId)
            : Path.Combine(downloadPath, channel.ChannelId);

        string extrasDir = Path.Combine(channelFolder, "_extras");

        if (playlist is not null)
        {
            extrasDir = Path.Combine(extrasDir, playlist.PlaylistId);
        }

        return extrasDir;
    }

    private string GetDownloadPath() =>
        configuration.GetValue<string>("VideoDownload:OutputPath")
        ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");

    private static AdditionalContentItemDto ToDto(AdditionalContentItem item) => new(
        item.Id,
        item.FileName,
        item.ContentType,
        item.FileSize,
        item.UploadedAt,
        item.ChannelId,
        item.PlaylistId,
        item.Playlist?.Name,
        item.VideoId,
        item.Video?.Title);

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
