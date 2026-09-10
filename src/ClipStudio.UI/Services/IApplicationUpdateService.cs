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
    /// Checks whether a newer release exists. Downloads nothing.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The resulting status, which is also published through <see cref="StatusChanged"/>.</returns>
    /// <remarks>
    /// Deliberately separate from <see cref="DownloadAsync"/>. An update package runs to hundreds
    /// of megabytes, and spending someone's bandwidth without asking is not a decision this
    /// application gets to make - particularly on a metered or shared connection.
    /// <para>
    /// Never throws. A network failure, an unreachable repository or an uninstalled build all
    /// resolve to a status rather than an exception, because an update check must never be able
    /// to disturb the running application.
    /// </para>
    /// </remarks>
    Task<UpdateStatus> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the update found by the last <see cref="CheckAsync"/>.
    /// </summary>
    /// <param name="progress">Receives the percentage complete, 0 to 100.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>
    /// <see langword="true"/> when the package finished downloading and can be installed;
    /// <see langword="false"/> when there was nothing to download or it failed.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// The download was cancelled. Every other failure resolves to <see langword="false"/> with
    /// the reason recorded on <see cref="Status"/>.
    /// </exception>
    Task<bool> DownloadAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs the downloaded update and restarts the application.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the restart was handed to the updater; <see langword="false"/>
    /// when there was nothing downloaded to apply.
    /// </returns>
    bool ApplyAndRestart();
}
