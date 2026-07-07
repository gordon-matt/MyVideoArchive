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

    [Fact]
    public void TryResolveChannelDownloadDirectory_PlatformChannel_ResolvesUnderRoot()
    {
        string root = @"D:\Videos\Archive";
        Assert.True(CustomChannelPathHelper.TryResolveChannelDownloadDirectory(
            root, "YouTube", "UCiEKDhv4v0YTc-ZtpABkXZA", out string? path));
        Assert.Equal(@"D:\Videos\Archive\UCiEKDhv4v0YTc-ZtpABkXZA", path);
    }

    [Fact]
    public void TryResolveChannelDownloadDirectory_CustomChannel_UsesCustomPrefix()
    {
        string root = @"D:\Videos\Archive";
        Assert.True(CustomChannelPathHelper.TryResolveChannelDownloadDirectory(
            root, "Custom", "My Channel", out string? path));
        Assert.Equal(@"D:\Videos\Archive\_Custom\My Channel", path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    public void TryResolveChannelDownloadDirectory_RejectsUnsafeChannelIds(string channelId)
    {
        Assert.False(CustomChannelPathHelper.TryResolveChannelDownloadDirectory(
            @"D:\Videos\Archive", "YouTube", channelId, out _));
    }

    [Fact]
    public void TryResolveChannelDownloadDirectory_RejectsDownloadRootItself()
    {
        Assert.False(CustomChannelPathHelper.TryResolveChannelDownloadDirectory(
            @"D:\Videos\Archive", "YouTube", "", out _));
    }

    [Fact]
    public void TryResolveChannelDownloadDirectory_ResolvedPathDeletesOnlyTargetFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), "mva-path-" + Guid.NewGuid().ToString("N"));
        string keepDir = Path.Combine(root, "UC-KEEP");
        string deleteDir = Path.Combine(root, "UC-DELETE");
        Directory.CreateDirectory(keepDir);
        Directory.CreateDirectory(deleteDir);
        File.WriteAllText(Path.Combine(keepDir, "keep.txt"), "keep");
        File.WriteAllText(Path.Combine(deleteDir, "delete.txt"), "delete");

        try
        {
            Assert.True(CustomChannelPathHelper.TryResolveChannelDownloadDirectory(
                root, "YouTube", "UC-DELETE", out string? resolved));
            Assert.Equal(Path.GetFullPath(deleteDir), resolved, ignoreCase: true);

            Directory.Delete(resolved!, recursive: true);

            Assert.False(Directory.Exists(deleteDir));
            Assert.True(Directory.Exists(keepDir));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }
}
