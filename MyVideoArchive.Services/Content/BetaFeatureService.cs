namespace MyVideoArchive.Services.Content;

/// <summary>
/// Exposes the <c>EnableBetaFeatures</c> setting. Beta features are still being stabilised and are
/// disabled by default; at present the beta-gated features are the <see cref="BetaPlatforms"/>
/// (their metadata providers and downloaders are only registered when beta features are enabled).
/// </summary>
public interface IBetaFeatureService
{
    /// <summary>
    /// Gets a value indicating whether beta features are enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Returns <c>true</c> if the given platform can be used, i.e. it is a stable platform or
    /// beta features are enabled.
    /// </summary>
    bool IsPlatformAvailable(string platform);
}

public class BetaFeatureService : IBetaFeatureService
{
    /// <summary>
    /// The configuration key (also settable via the <c>EnableBetaFeatures</c> environment variable).
    /// </summary>
    public const string ConfigurationKey = "EnableBetaFeatures";

    /// <summary>
    /// Platforms that are gated behind <see cref="ConfigurationKey"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> BetaPlatforms = ["Odysee", "Rumble"];

    public BetaFeatureService(IConfiguration configuration)
        : this(IsEnabledInConfiguration(configuration))
    {
    }

    public BetaFeatureService(bool isEnabled)
    {
        IsEnabled = isEnabled;
    }

    public bool IsEnabled { get; }

    public bool IsPlatformAvailable(string platform)
        => IsEnabled || !IsBetaPlatform(platform);

    public static bool IsBetaPlatform(string platform)
        => BetaPlatforms.Contains(platform, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads <c>EnableBetaFeatures</c> from configuration; missing or unparsable values mean <c>false</c>.
    /// </summary>
    public static bool IsEnabledInConfiguration(IConfiguration configuration)
        => bool.TryParse(configuration[ConfigurationKey], out bool enabled) && enabled;
}
