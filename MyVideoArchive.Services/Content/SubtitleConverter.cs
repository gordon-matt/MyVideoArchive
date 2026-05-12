using System.Text;
using System.Text.RegularExpressions;

namespace MyVideoArchive.Services.Content;

/// <summary>
/// Utility for converting SRT subtitle files to the WebVTT format used by HTML5 video players.
/// </summary>
public static partial class SubtitleConverter
{
    [GeneratedRegex(@"(\d{2}:\d{2}:\d{2}),(\d{3})")]
    private static partial Regex SrtTimestampRegex();

    public static string SrtToVtt(string srtContent)
    {
        string normalized = srtContent
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Trim();

        string converted = SrtTimestampRegex().Replace(normalized, "$1.$2");

        return "WEBVTT\n\n" + converted;
    }

    public static void ConvertFile(string srtPath, string vttPath)
    {
        string srtContent = File.ReadAllText(srtPath, Encoding.UTF8);
        string vttContent = SrtToVtt(srtContent);
        File.WriteAllText(vttPath, vttContent, Encoding.UTF8);
    }
}
