using MyVideoArchive.Services.Content.Providers;

namespace MyVideoArchive.Tests.Services;

public class RumblePageParserTests
{
    private static readonly string[] ExpectedDeusExVideoIds =
    [
        "v79rkwa",
        "v7aordq",
        "v7azpaw",
        "v7bbviu",
        "v7bof1o"
    ];

    [Fact]
    public void ParsePlaylistVideos_FindsAllFiveVideos_FromCapturedPlaylistHtml()
    {
        string html = File.ReadAllText(GetFixturePath("rumble-deus-ex-playlist.html"));

        var videos = RumblePageParser.ParsePlaylistVideos(
            html,
            "DQGhNlJnoBU",
            "Gibb Gaming",
            "Rumble");

        Assert.Equal(5, videos.Count);

        foreach (string expectedId in ExpectedDeusExVideoIds)
        {
            Assert.Contains(videos, v => v.VideoId.Equals(expectedId, StringComparison.OrdinalIgnoreCase));
        }

        Assert.Equal("Deus Ex - Week 1 - Welcome to the Coalition",
            videos.Single(v => v.VideoId == "v7aordq").Title);
        Assert.Equal("Deus Ex - Week 3 - Illuminati Confirmation",
            videos.Single(v => v.VideoId == "v7bbviu").Title);
    }

    [Fact]
    public void ParsePlaylistVideos_DecodesAmpersandInPlaylistIdQuery()
    {
        const string html = """
            <main>
              <ol class="videostream__list" data-playlist="TESTPL">
                <li class="videostream__details">
                  <div class="videostream videostream__list-item">
                    <a class="videostream__link" href="/v7aordq-deus-ex-week-1.html?e9s=src&amp;playlist_id=TESTPL"></a>
                  </div>
                </li>
              </ol>
            </main>
            """;

        var videos = RumblePageParser.ParsePlaylistVideos(html, "TESTPL", "Gibb Gaming", "Rumble");

        Assert.Single(videos);
        Assert.Equal("v7aordq", videos[0].VideoId);
    }

    [Fact]
    public void ParsePlaylistVideos_PrefersVideostreamListOverRecommendations()
    {
        const string html = """
            <main>
              <ol class="videostream__list" data-playlist="PL1">
                <li><div class="videostream"><a class="videostream__link" href="/v79rkwa-intro.html?playlist_id=PL1"></a></div></li>
              </ol>
              <aside class="sidebar recommend">
                <a href="/vOTHER-other-channel.html?playlist_id=PL1">Other</a>
              </aside>
            </main>
            <a href="/v79rkwa-intro.html?e9s=src_v1_pl&amp;playlist_id=PL1">dup</a>
            """;

        var videos = RumblePageParser.ParsePlaylistVideos(html, "PL1", "Gibb Gaming", "Rumble");

        Assert.Single(videos);
        Assert.Equal("v79rkwa", videos[0].VideoId);
    }

    private static string GetFixturePath(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Rumble", fileName);
        if (!File.Exists(path))
        {
            path = Path.Combine(
                Directory.GetCurrentDirectory(),
                "..",
                "..",
                "..",
                "Fixtures",
                "Rumble",
                fileName);
        }

        return Path.GetFullPath(path);
    }
}
