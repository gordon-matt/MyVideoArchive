namespace MyVideoArchive.Models.Requests;

public class DownloadVideosRequest
{
    public List<int> VideoIds { get; set; } = [];
}