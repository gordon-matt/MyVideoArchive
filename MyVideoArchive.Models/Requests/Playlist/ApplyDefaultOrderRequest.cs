using MyVideoArchive.Models.Responses;

namespace MyVideoArchive.Models.Requests.Playlist;

public class ApplyDefaultOrderRequest
{
    public List<VideoOrderItem>? VideoOrders { get; set; }
}
