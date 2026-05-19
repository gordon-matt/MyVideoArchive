using MyVideoArchive.Models.Responses;

namespace MyVideoArchive.Models.Requests.Playlist;

public class ReorderCustomPlaylistRequest
{
    public List<VideoOrderItem>? VideoOrders { get; set; }
}
