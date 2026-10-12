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

    /// <summary>
    /// Returns the on-disk folder for a channel's videos and thumbnails (write path).
    /// Platform channel IDs such as Odysee's <c>@name:8</c> are sanitized so paths are valid on Windows.
    /// Custom channels live under <c>_Custom/{channelId}</c>.
    /// </summary>
    public static string GetChannelDirectory(string downloadBasePath, string platform, string channelId)
    {
        string segment = SanitizeFolderNameSegment(channelId);
        if (string.IsNullOrEmpty(segment) || segment is "." or "..")
        {
            segment = "unknown-channel";
        }

        return string.Equals(platform, "Custom", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(downloadBasePath, "_Custom", segment)
            : Path.Combine(downloadBasePath, segment);
    }

    /// <summary>
    /// Resolves the on-disk folder for a channel's downloaded files and thumbnails.
    /// Custom channels live under <c>_Custom/{channelId}</c>; platform channels use <c>{channelId}</c>.
    /// </summary>
    /// <returns>
    /// <c>false</c> when <paramref name="channelId"/> is empty, path-traversal (<c>.</c>/<c>..</c>),
    /// or the resolved path would be the download root or outside it.
    /// </returns>
    public static bool TryResolveChannelDownloadDirectory(
        string downloadBasePath,
        string platform,
        string channelId,
        out string? directoryPath)
    {
        directoryPath = null;

        if (string.IsNullOrWhiteSpace(downloadBasePath) || string.IsNullOrWhiteSpace(channelId))
        {
            return false;
        }

        string segment = SanitizeFolderNameSegment(channelId);
        if (string.IsNullOrEmpty(segment) || segment is "." or "..")
        {
            return false;
        }

        if (segment.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || segment.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            return false;
        }

        string relativePath = string.Equals(platform, "Custom", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine("_Custom", segment)
            : segment;

        string rootFullPath = Path.GetFullPath(downloadBasePath);
        string candidateFullPath = Path.GetFullPath(Path.Combine(downloadBasePath, relativePath));

        if (!IsStrictChildDirectory(rootFullPath, candidateFullPath))
        {
            return false;
        }

        directoryPath = candidateFullPath;
        return true;
    }

    private static bool IsStrictChildDirectory(string rootFullPath, string candidateFullPath)
    {
        if (!candidateFullPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (candidateFullPath.Length <= rootFullPath.Length)
        {
            return false;
        }

        char separator = candidateFullPath[rootFullPath.Length];
        return separator == Path.DirectorySeparatorChar || separator == Path.AltDirectorySeparatorChar;
    }
}