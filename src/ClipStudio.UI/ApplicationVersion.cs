using System.Reflection;

namespace ClipStudio.UI;

/// <summary>
/// The running application's version, as it should be shown to a person.
/// </summary>
/// <remarks>
/// Read from <see cref="AssemblyInformationalVersionAttribute"/> rather than from
/// <see cref="AssemblyName.Version"/>. The release workflow stamps the tag onto the build with
/// <c>-p:Version=</c>, which flows into the informational version, while
/// <c>AssemblyVersion</c>/<c>FileVersion</c> can be pinned in the project file and then report a
/// number that has nothing to do with the release - which is exactly what happened: every build
/// identified itself as v0.1.0, including in the environment block attached to bug reports.
/// </remarks>
internal static class ApplicationVersion
{
    /// <summary>Gets the version alone, such as <c>1.1.4</c>.</summary>
    public static string Current { get; } = Resolve();

    /// <summary>Gets the product and version together, such as <c>ClipStudio v1.1.4</c>.</summary>
    public static string Display { get; } = $"ClipStudio v{Current}";

    /// <summary>Reads the version out of the assembly's metadata.</summary>
    /// <returns>The version string, or <c>unknown</c> when it cannot be determined.</returns>
    private static string Resolve()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Build metadata after '+' is the commit hash the SDK appends; it is not part of the
            // version a person should be shown.
            var metadata = informational.IndexOf('+');
            return metadata < 0 ? informational : informational[..metadata];
        }

        var version = assembly.GetName().Version;
        return version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
