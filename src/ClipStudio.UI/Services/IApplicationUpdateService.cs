using System;
using System.Threading;
using System.Threading.Tasks;

namespace ClipStudio.UI.Services;

/// <summary>
/// Looks for newer releases of the application, downloads them, and applies them on request.
/// </summary>
/// <remarks>
/// Deliberately narrow, and deliberately not automatic past the download: the check and the
/// download happen on their own in the background, but installing an update restarts the
/// application, and that is never something to do behind the user's back while they are working.
/// <para>
/// Abstracted so the Settings notification can be exercised with a fake, without a real
/// installation or a network.
/// </para>
/// </remarks>
public interface IApplicationUpdateService
{
    /// <summary>Gets what the last check found.</summary>
    UpdateStatus Status { get; }

    /// <summary>Raised whenever <see cref="Status"/> is replaced.</summary>
    event EventHandler<UpdateStatus>? StatusChanged;

    /// <summary>
    /// Checks for a newer release and, if one exists, downloads it in the background.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check and any download in progress.</param>
    /// <returns>The resulting status, which is also published through <see cref="StatusChanged"/>.</returns>
    /// <remarks>
    /// Never throws. A network failure, an unreachable repository or an uninstalled build all
    /// resolve to a status rather than an exception, because an update check must never be able
    /// to disturb the running application.
    /// </remarks>
    Task<UpdateStatus> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs the downloaded update and restarts the application.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the restart was handed to the updater; <see langword="false"/>
    /// when there was nothing downloaded to apply.
    /// </returns>
    bool ApplyAndRestart();
}
