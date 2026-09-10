using System;
using System.Diagnostics;
using System.IO;

namespace ClipStudio.UI;

/// <summary>
/// Restarts the application once on macOS with the environment LibVLC needs in order to load.
/// </summary>
/// <remarks>
/// VLC's <c>libvlc.dylib</c> pulls in <c>libvlccore.dylib</c> through the dynamic loader, which
/// reads <c>DYLD_LIBRARY_PATH</c> at process start and cannot be influenced afterwards - so
/// <c>Core.Initialize</c> is not on its own enough, and LaunchServices strips <c>DYLD_*</c> from
/// anything it spawns. The previous fix was a shell script installed as the bundle executable,
/// which exported the variables and then exec'd the real binary.
/// <para>
/// Velopack builds a real <c>.app</c> bundle and requires its main executable to be a Mach-O
/// binary, so a shell script cannot be the entry point any more. Doing the same job from inside
/// the binary keeps the behaviour and satisfies the packager: the first process sets the variables
/// and starts a second copy of itself, which finds VLC and shows the UI.
/// </para>
/// <para>
/// A sentinel variable makes this happen at most once, so a failure to propagate the environment
/// cannot turn into a loop of processes.
/// </para>
/// </remarks>
internal static class MacOsVlcRelauncher
{
    /// <summary>Marks a process that has already been started with the VLC environment set.</summary>
    private const string SentinelVariable = "CLIPSTUDIO_VLC_ENV_READY";

    /// <summary>The directory VLC installs its Mach-O binaries and plugins under.</summary>
    private const string VlcBaseDirectory = "/Applications/VLC.app/Contents/MacOS";

    /// <summary>
    /// Starts a second copy of this process with the VLC environment set, when that is both
    /// needed and possible.
    /// </summary>
    /// <param name="args">The command-line arguments to pass on unchanged.</param>
    /// <returns>
    /// <see langword="true"/> when a replacement process was started and this one should exit
    /// without showing a window; <see langword="false"/> when the application should carry on.
    /// </returns>
    /// <remarks>
    /// Returns <see langword="false"/> on every platform but macOS, when the environment is
    /// already set, and when VLC is not installed - in that last case there is nothing to point
    /// at, and the application still starts so it can report the missing dependency itself rather
    /// than dying silently at launch.
    /// </remarks>
    public static bool RelaunchIfNeeded(string[] args)
    {
        if (!OperatingSystem.IsMacOS())
            return false;

        if (Environment.GetEnvironmentVariable(SentinelVariable) == "1")
            return false;

        var libraryDirectory = Path.Combine(VlcBaseDirectory, "lib");
        if (!Directory.Exists(libraryDirectory))
            return false;

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executablePath))
            return false;

        try
        {
            var startInfo = new ProcessStartInfo(executablePath) { UseShellExecute = false };

            foreach (var argument in args)
                startInfo.ArgumentList.Add(argument);

            var existingPath = Environment.GetEnvironmentVariable("DYLD_LIBRARY_PATH");

            startInfo.Environment["DYLD_LIBRARY_PATH"] = string.IsNullOrEmpty(existingPath)
                ? libraryDirectory
                : $"{libraryDirectory}:{existingPath}";
            startInfo.Environment["VLC_PLUGIN_PATH"] = Path.Combine(VlcBaseDirectory, "plugins");
            startInfo.Environment[SentinelVariable]  = "1";

            return Process.Start(startInfo) is not null;
        }
        catch (Exception ex)
        {
            // Carrying on without the environment is better than not starting at all: playback
            // may fail, but the rest of the library remains usable and the log says why.
            Serilog.Log.Warning(ex, "Could not restart with the VLC environment set.");
            return false;
        }
    }
}
