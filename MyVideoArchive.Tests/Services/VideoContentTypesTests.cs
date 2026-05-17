using MyVideoArchive.Services.Content;

namespace MyVideoArchive.Tests.Services;

public class VideoContentTypesTests
{
    [Theory]
    [InlineData(".flv", "video/x-flv")]
    [InlineData(".FLV", "video/x-flv")]
    [InlineData(".mp4", "video/mp4")]
    [InlineData("C:\\archive\\movie.flv", "video/x-flv")]
    public void FromFilePath_ReturnsExpectedMimeType(string input, string expected)
    {
        string actual = input.Contains('\\') || input.Contains('/')
            ? VideoContentTypes.FromFilePath(input)
            : VideoContentTypes.FromExtension(input);

        Assert.Equal(expected, actual);
    }
}
