namespace MyVideoArchive.Models.Requests.AdditionalContent;

public record UpdateAdditionalContentRequest(
    string FileName,
    IReadOnlyList<int>? PlaylistIds);

public record LinkAdditionalContentToVideoRequest(IReadOnlyList<int> ItemIds);
