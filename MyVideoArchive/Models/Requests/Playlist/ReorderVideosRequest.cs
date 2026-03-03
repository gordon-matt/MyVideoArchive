using MyVideoArchive.Models.Responses;

namespace MyVideoArchive.Models.Requests.Playlist;

public class ReorderVideosRequest
{
    public bool UseCustomOrder { get; set; }

    public List<VideoOrderItem>? VideoOrders { get; set; }
}