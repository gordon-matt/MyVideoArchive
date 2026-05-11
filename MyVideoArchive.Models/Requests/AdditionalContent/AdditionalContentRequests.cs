namespace MyVideoArchive.Models.Requests.AdditionalContent;

public record UpdateAdditionalContentRequest(
    string FileName,
    int? PlaylistId,
    int? VideoId);
