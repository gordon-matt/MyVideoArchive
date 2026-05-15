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
}
