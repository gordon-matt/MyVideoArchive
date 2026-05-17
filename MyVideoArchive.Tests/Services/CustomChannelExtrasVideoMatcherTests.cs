using MyVideoArchive.Services.Content;

namespace MyVideoArchive.Tests.Services;

public class CustomChannelExtrasVideoMatcherTests
{
    [Theory]
    [InlineData("CS50 Introduction to Game Development/01 - Pong", "01 - Pong", true)]
    [InlineData("01 - Pong", "01 - Pong", true)]
    [InlineData("CS50 Introduction to Game Development/02 - Flappy Bird", "01 - Pong", false)]
    public void VideoIdMatchesExtrasFolder_Works(string videoId, string folder, bool expected)
    {
        Assert.Equal(expected, CustomChannelExtrasVideoMatcher.VideoIdMatchesExtrasFolder(videoId, folder));
    }

    [Fact]
    public void TryResolveDatabaseVideoId_MatchesPathBasedVideoId()
    {
        string channel = Path.Combine(Path.GetTempPath(), "ch-" + Guid.NewGuid().ToString("N"));
        string playlist = Path.Combine(channel, "CS50 Introduction to Game Development");
        string extras = Path.Combine(playlist, "_extras", "01 - Pong");
        Directory.CreateDirectory(extras);
        string file = Path.Combine(extras, "notes.pdf");
        File.WriteAllText(file, "x");

        try
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["CS50 Introduction to Game Development/01 - Pong"] = 42
            };

            int? id = CustomChannelExtrasVideoMatcher.TryResolveDatabaseVideoId(channel, file, map);
            Assert.Equal(42, id);
        }
        finally
        {
            try { Directory.Delete(channel, true); } catch { /* ignore */ }
        }
    }
}
