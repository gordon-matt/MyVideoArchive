using System.Text;

namespace MyVideoArchive.Services;

/// <summary>
/// Builds filesystem-safe folder / channel ID segments for custom channels.
/// </summary>
public static class CustomChannelPathHelper
{
    /// <summary>
    /// Removes characters that are invalid in file or path segments (see <see cref="Path.GetInvalidPathChars"/>
    /// and <see cref="Path.GetInvalidFileNameChars"/>), trims, and trims trailing spaces and dots (Windows).
    /// </summary>
    public static string SanitizeFolderNameSegment(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var invalid = new HashSet<char>();
        invalid.AddRange(Path.GetInvalidPathChars());
        invalid.AddRange(Path.GetInvalidFileNameChars());

        var sb = new StringBuilder(name.Trim().Length);
        foreach (char ch in name.Trim())
        {
            if (!invalid.Contains(ch))
            {
                sb.Append(ch);
            }
        }

        string s = sb.ToString().TrimEnd(' ', '.');
        const int maxLen = 120;
        if (s.Length > maxLen)
        {
            s = s[..maxLen].TrimEnd(' ', '.');
        }

        return s;
    }
}