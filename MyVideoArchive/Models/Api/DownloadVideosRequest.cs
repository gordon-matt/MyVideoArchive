namespace MyVideoArchive.Models.Api;

public class DownloadVideosRequest
{
    public List<int> VideoIds { get; set; } = [];
}