namespace MyVideoArchive.Models.Api;

public class SetVideoTagsRequest
{
    public List<string> TagNames { get; set; } = [];
}