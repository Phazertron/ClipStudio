using ClipStudio.Application.Models;
using ClipStudio.Core.Enums;

namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Reads the titles a game launcher has installed on this machine.
/// </summary>
/// <remarks>
/// One implementation per launcher, so a launcher that is not installed - or not supported on this
/// platform - simply reports itself unavailable instead of every caller having to know which
/// launchers exist where.
/// </remarks>
public interface IInstalledGameScanner
{
    /// <summary>Gets which launcher this scanner reads.</summary>
    GameLauncher Launcher { get; }

    /// <summary>Gets whether this launcher is present and readable on this machine.</summary>
    /// <remarks>
    /// Cheap to check - it looks for the launcher's data, not for its games - so a caller can ask
    /// before offering an import that would find nothing.
    /// </remarks>
    bool IsAvailable { get; }

    /// <summary>
    /// Reads the installed titles.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>
    /// The titles found, including the ones flagged as probably not games. Empty when the launcher
    /// is unavailable or its data could not be read.
    /// </returns>
    /// <remarks>
    /// Read-only throughout: nothing about the launcher's own files is written or moved.
    /// </remarks>
    Task<IReadOnlyList<InstalledGame>> ScanAsync(CancellationToken cancellationToken = default);
}
