using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Velopack;
using Velopack.Sources;

namespace ClipStudio.UI.Services;

/// <summary>
/// An <see cref="IApplicationUpdateService"/> backed by Velopack, reading releases from a GitHub
/// repository's published releases.
/// </summary>
/// <remarks>
/// The repository is resolved through <see cref="GithubSource"/> rather than being handed to
/// <see cref="UpdateManager"/> as a bare string. A bare string is treated as a plain web feed and
/// would request <c>&lt;repo&gt;/releases.&lt;channel&gt;.json</c>, a path that does not exist on
/// github.com; the resulting 404 surfaces as an exception on every single check.
/// <para>
/// Velopack picks the update channel from the installed package's own metadata, so a Windows
/// install follows the <c>win</c> channel and never sees the macOS or Linux packages published
/// under the same release.
/// </para>
/// </remarks>
public sealed class VelopackUpdateService : IApplicationUpdateService
{
    private readonly string? _repositoryUrl;
    private readonly bool _includePrerelease;

    private UpdateManager? _manager;
    private UpdateInfo? _pendingUpdate;

    /// <inheritdoc/>
    public UpdateStatus Status { get; private set; } = UpdateStatus.Unsupported;

    /// <inheritdoc/>
    public event EventHandler<UpdateStatus>? StatusChanged;

    /// <summary>Initialises a new <see cref="VelopackUpdateService"/>.</summary>
    /// <param name="repositoryUrl">
    /// The GitHub repository to read releases from, or <see langword="null"/> to disable update
    /// checks entirely.
    /// </param>
    /// <param name="includePrerelease">
    /// Whether releases marked as pre-releases are offered. False so a stable installation is
    /// never moved onto a beta build.
    /// </param>
    public VelopackUpdateService(string? repositoryUrl, bool includePrerelease = false)
    {
        _repositoryUrl     = repositoryUrl;
        _includePrerelease = includePrerelease;
    }

    /// <inheritdoc/>
    public async Task<UpdateStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (_repositoryUrl is null)
            return Publish(UpdateStatus.Unsupported);

        try
        {
            var manager = _manager ??= new UpdateManager(
                new GithubSource(_repositoryUrl, accessToken: null, prerelease: _includePrerelease));

            if (!manager.IsInstalled)
            {
                // A publish folder or a debugger session. There is no installation to update,
                // which is not a problem and must not be reported as one.
                Log.Debug("Update check skipped: this build is not a Velopack installation.");
                return Publish(UpdateStatus.Unsupported);
            }

            var current = manager.CurrentVersion?.ToString();
            var update  = await manager.CheckForUpdatesAsync().ConfigureAwait(false);

            if (update is null)
            {
                Log.Information("Update check complete: {Version} is current.", current);
                return Publish(new UpdateStatus { IsSupported = true, CurrentVersion = current });
            }

            var available = update.TargetFullRelease.Version.ToString();
            Log.Information("Update available: {Version}. Waiting to be asked before downloading.", available);

            _pendingUpdate = update;

            return Publish(new UpdateStatus
            {
                IsSupported      = true,
                CurrentVersion   = current,
                AvailableVersion = available,
                IsDownloaded     = false,
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Best-effort by design, but logged rather than swallowed: a silent catch here hid a
            // permanently broken update feed for several releases.
            Log.Warning(ex, "Update check failed.");
            return Publish(new UpdateStatus
            {
                IsSupported   = true,
                FailureReason = ex.Message,
            });
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DownloadAsync(
        IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_manager is null || _pendingUpdate is null)
            return false;

        var available = _pendingUpdate.TargetFullRelease.Version.ToString();
        var current   = Status.CurrentVersion;

        Publish(new UpdateStatus
        {
            IsSupported      = true,
            CurrentVersion   = current,
            AvailableVersion = available,
            IsDownloading    = true,
        });

        try
        {
            Log.Information("Downloading update {Version}.", available);

            await _manager.DownloadUpdatesAsync(
                _pendingUpdate,
                progress: percent => progress?.Report(percent),
                cancelToken: cancellationToken).ConfigureAwait(false);

            Log.Information("Update {Version} downloaded and ready to install.", available);

            Publish(new UpdateStatus
            {
                IsSupported      = true,
                CurrentVersion   = current,
                AvailableVersion = available,
                IsDownloaded     = true,
            });

            return true;
        }
        catch (OperationCanceledException)
        {
            // A cancelled download leaves the offer standing so it can be taken up later.
            Log.Information("Update download cancelled.");
            Publish(new UpdateStatus
            {
                IsSupported      = true,
                CurrentVersion   = current,
                AvailableVersion = available,
            });
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update download failed.");
            Publish(new UpdateStatus
            {
                IsSupported      = true,
                CurrentVersion   = current,
                AvailableVersion = available,
                FailureReason    = ex.Message,
            });
            return false;
        }
    }

    /// <inheritdoc/>
    public bool ApplyAndRestart()
    {
        if (_manager is null || _pendingUpdate is null)
            return false;

        Log.Information("Applying update {Version} and restarting.",
            _pendingUpdate.TargetFullRelease.Version);

        _manager.ApplyUpdatesAndRestart(_pendingUpdate);
        return true;
    }

    /// <summary>Replaces <see cref="Status"/> and notifies listeners.</summary>
    /// <param name="status">The new status.</param>
    /// <returns>The status that was published, for convenient returning.</returns>
    private UpdateStatus Publish(UpdateStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
        return status;
    }
}
