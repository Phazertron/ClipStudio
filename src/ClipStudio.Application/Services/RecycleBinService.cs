using System.Diagnostics;
using ClipStudio.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ClipStudio.Application.Services;

/// <summary>
/// Cross-platform implementation of <see cref="IRecycleBinService"/>.
/// <list type="bullet">
///   <item><term>Windows</term><description>Uses <c>Microsoft.VisualBasic.FileIO.FileSystem</c> to send to the Recycle Bin.</description></item>
///   <item><term>macOS</term><description>Uses <c>osascript</c> to invoke the Finder trash action.</description></item>
///   <item><term>Linux</term><description>Uses <c>gio trash</c> (part of GLib, widely available on major distros). Falls back to permanent delete if <c>gio</c> is not installed.</description></item>
/// </list>
/// On all platforms, if the recycle operation fails, the file is permanently deleted as a fallback
/// so that the caller can always proceed without error handling for missing-file scenarios.
/// </summary>
public sealed class RecycleBinService : IRecycleBinService
{
    private readonly ILogger<RecycleBinService> _logger;

    /// <summary>Initializes a new instance of <see cref="RecycleBinService"/>.</summary>
    public RecycleBinService(ILogger<RecycleBinService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool TryMoveToRecycleBin(string path)
    {
        if (!File.Exists(path))
            return false;

        try
        {
            if (OperatingSystem.IsWindows())
                return RecycleWindows(path);

            if (OperatingSystem.IsMacOS())
                return RecycleMacOs(path);

            return RecycleLinux(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Recycle bin move failed for {Path}; falling back to permanent delete.", path);
            FallbackDelete(path);
            return false;
        }
    }

    private bool RecycleWindows(string path)
    {
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
            path,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

        return true;
    }

    private bool RecycleMacOs(string path)
    {
        // Escape single-quotes in the path so the AppleScript string is safe.
        var escaped = path.Replace("'", "'\\''");
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName               = "osascript",
            Arguments              = $"-e 'tell application \"Finder\" to delete POSIX file \"{escaped}\"'",
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            CreateNoWindow         = true,
        });

        var exited = proc?.WaitForExit(10_000) ?? false;

        if (!exited || proc?.ExitCode != 0)
        {
            _logger.LogWarning("osascript trash failed for {Path}; falling back to permanent delete.", path);
            FallbackDelete(path);
            return false;
        }

        return true;
    }

    private bool RecycleLinux(string path)
    {
        // gio trash is the modern freedesktop.org standard for moving files to the trash.
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName        = "gio",
            ArgumentList    = { "trash", path },
            UseShellExecute = false,
            CreateNoWindow  = true,
        });

        var exited = proc?.WaitForExit(10_000) ?? false;

        if (!exited || proc?.ExitCode != 0)
        {
            _logger.LogWarning("gio trash failed for {Path}; falling back to permanent delete.", path);
            FallbackDelete(path);
            return false;
        }

        return true;
    }

    private void FallbackDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallback permanent delete also failed for {Path}.", path);
        }
    }
}
