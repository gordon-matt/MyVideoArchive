namespace MyVideoArchive.Services.Content;

/// <summary>
/// Rules for which folder names the custom-channel file-system scan should ignore
/// when detecting series/playlist layout (e.g. Synology DSM metadata directories).
/// </summary>
public static class CustomChannelFolderRules
{
    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "@eadir",
        "#recycle",
        "@sharebin",
        "@tmp",
        ".@__thumb",
        ".synologyworkingdirectory"
    };

    /// <summary>
    /// True when a directory should not be treated as a series, playlist, or structural subfolder.
    /// Matches names starting with <c>_</c> (app convention) and known NAS/OS metadata folders.
    /// </summary>
    public static bool IsIgnoredDirectoryName(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return true;
        }

        if (folderName.StartsWith("_", StringComparison.Ordinal))
        {
            return true;
        }

        return IgnoredDirectoryNames.Contains(folderName);
    }

    /// <summary>
    /// True when a file should not be imported or listed as additional content (Synology streams, paths under @eaDir, etc.).
    /// Only matches known NAS metadata directory names — not every <c>_</c> folder (e.g. <c>_extras</c>, <c>_Custom</c> are fine).
    /// </summary>
    public static bool IsIgnoredAdditionalContentPath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return true;
        }

        string fileName = Path.GetFileName(filePath);
        if (IsSynologyMetadataFileName(fileName))
        {
            return true;
        }

        try
        {
            string full = Path.GetFullPath(filePath);
            string[] parts = full.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);

            foreach (string part in parts)
            {
                if (IsNasMetadataDirectorySegment(part))
                {
                    return true;
                }
            }
        }
        catch
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// NAS/OS metadata directories in a file path (e.g. <c>@eaDir</c>). Does not use the series/playlist <c>_</c> prefix rule.
    /// </summary>
    internal static bool IsNasMetadataDirectorySegment(string? segment) =>
        !string.IsNullOrWhiteSpace(segment) && IgnoredDirectoryNames.Contains(segment);

    /// <summary>
    /// Synology DSM may expose extended-attribute data as separate file names ending with <c>@SynoEAStream</c>.
    /// </summary>
    public static bool IsSynologyMetadataFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return true;
        }

        if (fileName.Contains("@SynoEAStream", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (fileName.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
