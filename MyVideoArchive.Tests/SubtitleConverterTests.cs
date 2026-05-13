namespace MyVideoArchive.Tests;

public class SubtitleConverterTests
{
    [Fact]
    public void SrtToVtt_ReplacesCommaMillisecondsAndPrefixesWebvtt()
    {
        const string srt = "1\n00:00:01,234 --> 00:00:02,567\nHello\n";
        string vtt = SubtitleConverter.SrtToVtt(srt);

        Assert.StartsWith("WEBVTT", vtt, StringComparison.Ordinal);
        Assert.Contains("00:00:01.234", vtt, StringComparison.Ordinal);
        Assert.Contains("00:00:02.567", vtt, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertFile_WritesVttNextToSrt()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mva-sub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string srtPath = Path.Combine(dir, "a.srt");
        string vttPath = Path.Combine(dir, "a.vtt");
        File.WriteAllText(srtPath, "1\n00:00:00,000 --> 00:00:01,000\nHi\n", System.Text.Encoding.UTF8);
        try
        {
            SubtitleConverter.ConvertFile(srtPath, vttPath);
            string vtt = File.ReadAllText(vttPath, System.Text.Encoding.UTF8);
            Assert.StartsWith("WEBVTT", vtt, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}