namespace MyVideoArchive.Models.Api;

public class ReorderVideosRequest
{
    public bool UseCustomOrder { get; set; }

    public List<VideoOrderItem>? VideoOrders { get; set; }
}