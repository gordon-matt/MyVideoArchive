namespace MyVideoArchive;

public static class Constants
{
    public const string BestDownloadQuality = "bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best";

    public const string DeletedVideoTitle = "[Deleted video]";
    public const string PrivateVideoTitle = "[Private video]";

    public const string StandaloneTag = "standalone";

    public static class Roles
    {
        public const string Administrator = "Administrator";
        public const string User = "User";
    }
}