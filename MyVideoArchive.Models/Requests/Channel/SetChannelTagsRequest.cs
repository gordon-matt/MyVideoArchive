namespace MyVideoArchive.Models.Requests.Channel;

public class SetChannelTagsRequest
{
    public List<string> TagNames { get; set; } = [];
}
