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
}
