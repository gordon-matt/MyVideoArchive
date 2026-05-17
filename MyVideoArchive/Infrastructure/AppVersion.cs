using System.Reflection;

namespace MyVideoArchive.Infrastructure;

/// <summary>
/// Application version from the web project <c>&lt;Version&gt;</c> MSBuild property.
/// </summary>
public static class AppVersion
{
    private static readonly Lazy<string> CurrentLazy = new(Resolve);

    public static string Current => CurrentLazy.Value;

    private static string Resolve()
    {
        Assembly assembly = typeof(AppVersion).Assembly;

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            int plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? informational[..plus] : informational;
        }

        Version? version = assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
