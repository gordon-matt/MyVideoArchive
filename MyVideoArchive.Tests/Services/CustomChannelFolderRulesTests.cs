using MyVideoArchive.Services.Content;

namespace MyVideoArchive.Tests.Services;

public class CustomChannelFolderRulesTests
{
    [Theory]
    [InlineData("_extras")]
    [InlineData("_images")]
    [InlineData("@eaDir")]
    [InlineData("@eadir")]
    [InlineData("#recycle")]
    [InlineData("@sharebin")]
    public void IsIgnoredDirectoryName_KnownSystemFolders_ReturnsTrue(string name)
    {
        Assert.True(CustomChannelFolderRules.IsIgnoredDirectoryName(name));
    }

    [Theory]
    [InlineData("CS50 Introduction to Game Development")]
    [InlineData("edX_HarvardX")]
    [InlineData("Lecture 01")]
    public void IsIgnoredDirectoryName_NormalContentFolders_ReturnsFalse(string name)
    {
        Assert.False(CustomChannelFolderRules.IsIgnoredDirectoryName(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsIgnoredDirectoryName_NullOrWhitespace_ReturnsTrue(string? name)
    {
        Assert.True(CustomChannelFolderRules.IsIgnoredDirectoryName(name));
    }

    [Fact]
    public void IsIgnoredAdditionalContentPath_WhenUnderEaDir_ReturnsTrue()
    {
        string path = @"D:\Videos\_Custom\ch\Course\@eaDir\01 - Pong.mp4@SynoEAStream";
        Assert.True(CustomChannelFolderRules.IsIgnoredAdditionalContentPath(path));
    }

    [Fact]
    public void IsIgnoredAdditionalContentPath_WhenUnderExtrasFolder_ReturnsFalse()
    {
        string path = @"D:\Videos\_Custom\ch\Course\_extras\01 - Pong\notes.pdf";
        Assert.False(CustomChannelFolderRules.IsIgnoredAdditionalContentPath(path));
    }

    [Fact]
    public void IsIgnoredAdditionalContentPath_WhenOnlyExtrasAndVideoFolder_ReturnsFalse()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "_Custom",
            "edX",
            "CS50 Introduction to Game Development",
            "_extras",
            "01 - Pong",
            "readme.pdf");
        Assert.False(CustomChannelFolderRules.IsIgnoredAdditionalContentPath(path));
    }

    [Theory]
    [InlineData("01 - Pong.mp4@SynoEAStream")]
    [InlineData("readme@SynoEAStream")]
    public void IsSynologyMetadataFileName_StreamSuffix_ReturnsTrue(string name)
    {
        Assert.True(CustomChannelFolderRules.IsSynologyMetadataFileName(name));
    }
}
