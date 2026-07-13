using System.Reflection;

namespace Segrep.Update;

/// <summary>
/// Single source of truth for the running build's version. Release builds stamp the version
/// from the git tag via <c>-p:Version=&lt;tag&gt;</c> (see <c>.github/workflows/release.yml</c>);
/// local builds fall back to the <c>&lt;Version&gt;</c> dev floor in the csproj.
/// </summary>
public static class VersionInfo
{
    /// <summary>The version as a display string, e.g. "0.2.0".</summary>
    public static string Current { get; } = ReadCurrent();

    /// <summary>The version parsed for comparison against a release tag.</summary>
    public static Version CurrentVersion { get; } =
        Version.TryParse(Current, out var version) ? version : new Version(0, 0, 0);

    private static string ReadCurrent()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(VersionInfo).Assembly;

        // Prefer the informational version (what -p:Version stamps); strip any "+buildmetadata".
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
