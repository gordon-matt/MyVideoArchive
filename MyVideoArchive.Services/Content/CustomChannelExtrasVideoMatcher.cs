namespace MyVideoArchive.Services.Content;

/// <summary>
/// Resolves a video database id for files under a channel's <c>_extras</c> tree.
/// </summary>
public static class CustomChannelExtrasVideoMatcher
{
    /// <summary>
    /// Matches the folder name immediately after the last <c>_extras</c> segment in <paramref name="extrasFilePath"/>
    /// to a <see cref="Data.Entities.Video.VideoId"/> (supports path-based ids such as <c>Course/01 - Pong</c>).
    /// </summary>
    public static int? TryResolveDatabaseVideoId(
        string channelPath,
        string extrasFilePath,
        IReadOnlyDictionary<string, int> videoIdToDbId)
    {
        if (videoIdToDbId.Count == 0)
        {
            return null;
        }

        string? extrasFolderName = TryGetVideoFolderNameAfterExtras(channelPath, extrasFilePath);
        if (string.IsNullOrEmpty(extrasFolderName))
        {
            return null;
        }

        if (videoIdToDbId.TryGetValue(extrasFolderName, out int directId))
        {
            return directId;
        }

        foreach (var (videoId, dbId) in videoIdToDbId)
        {
            if (VideoIdMatchesExtrasFolder(videoId, extrasFolderName))
            {
                return dbId;
            }
        }

        return null;
    }

    internal static bool VideoIdMatchesExtrasFolder(string videoId, string extrasFolderName)
    {
        if (string.IsNullOrEmpty(videoId) || string.IsNullOrEmpty(extrasFolderName))
        {
            return false;
        }

        if (string.Equals(videoId, extrasFolderName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        int lastSlash = videoId.LastIndexOf('/');
        if (lastSlash >= 0 &&
            string.Equals(videoId[(lastSlash + 1)..], extrasFolderName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        int lastBackslash = videoId.LastIndexOf('\\');
        if (lastBackslash >= 0 &&
            string.Equals(videoId[(lastBackslash + 1)..], extrasFolderName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string? TryGetVideoFolderNameAfterExtras(string channelPath, string filePath)
    {
        try
        {
            string channelRoot = Path.GetFullPath(channelPath);
            string full = Path.GetFullPath(filePath);
            if (!full.StartsWith(channelRoot, StringComparison.OrdinalIgnoreCase) || full.Length <= channelRoot.Length)
            {
                return null;
            }

            string rel = Path.GetRelativePath(channelRoot, full);
            if (string.IsNullOrEmpty(rel) || rel == "." || rel.StartsWith("..", StringComparison.Ordinal))
            {
                return null;
            }

            string[] parts = rel.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);

            int extrasIdx = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Equals("_extras", StringComparison.OrdinalIgnoreCase))
                {
                    extrasIdx = i;
                }
            }

            if (extrasIdx < 0 || extrasIdx + 2 >= parts.Length)
            {
                return null;
            }

            return parts[extrasIdx + 1];
        }
        catch
        {
            return null;
        }
    }
}
