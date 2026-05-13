namespace MyVideoArchive.Tests.Services;

public class CustomChannelPathHelperTests
{
    [Theory]
    [InlineData("My Cool Channel", "My Cool Channel")]
    [InlineData("  Trimmed  ", "Trimmed")]
    [InlineData("a\u0000b", "ab")]
    public void SanitizeFolderNameSegment_RemovesInvalidCharacters(string input, string expected)
    {
        Assert.Equal(expected, CustomChannelPathHelper.SanitizeFolderNameSegment(input));
    }
}
