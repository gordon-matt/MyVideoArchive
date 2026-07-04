namespace MyVideoArchive;

public static class Constants
{
    public const string BestDownloadQuality = "bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best";

    public const string DeletedVideoTitle = "[Deleted video]";
    public const string PrivateVideoTitle = "[Private video]";

    /// <summary>
    /// Sentinel UserId stored on <see cref="Data.Entities.Tag"/> records that are global/system-wide
    /// (i.e. admin-created tags visible to all users as suggestions).
    /// </summary>
    public const string GlobalUserId = "_global";

    public const string StandaloneTag = "standalone";

    public static class Roles
    {
        public const string Administrator = "Administrator";
        public const string User = "User";
    }

    /// <summary>
    /// Supported database providers, selected via the <c>Database:Provider</c> configuration key.
    /// Each value maps to a <c>MyVideoArchive.Data.{Provider}</c> project. Defaults to
    /// <see cref="Npgsql"/> to preserve behaviour for existing PostgreSQL deployments.
    /// </summary>
    public static class DatabaseProviders
    {
        public const string MySql = "MySql";
        public const string Npgsql = "Npgsql";
        public const string Sqlite = "Sqlite";
        public const string SqlServer = "SqlServer";
    }
}