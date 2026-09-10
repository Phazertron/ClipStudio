using System;
using System.Threading;
using System.Threading.Tasks;
using ClipStudio.UI.Services;

namespace ClipStudio.Tests.Fakes;

/// <summary>
/// An <see cref="IApplicationUpdateService"/> whose status is set directly by a test, so the
/// update notification can be exercised without an installation, a network or a real restart.
/// </summary>
public sealed class FakeApplicationUpdateService : IApplicationUpdateService
{
    /// <inheritdoc/>
    public UpdateStatus Status { get; private set; } = UpdateStatus.Unsupported;

    /// <inheritdoc/>
    public event EventHandler<UpdateStatus>? StatusChanged;

    /// <summary>Gets how many times <see cref="ApplyAndRestart"/> was called.</summary>
    public int ApplyCount { get; private set; }

    /// <summary>Gets how many times <see cref="DownloadAsync"/> was called.</summary>
    public int DownloadCount { get; private set; }

    /// <summary>Gets or sets what <see cref="DownloadAsync"/> returns.</summary>
    public bool DownloadResult { get; set; } = true;

    /// <summary>Gets or sets percentages reported before the download completes.</summary>
    public int[] DownloadProgressSteps { get; set; } = [];

    /// <summary>
    /// Gets or sets whether <see cref="DownloadAsync"/> throws
    /// <see cref="OperationCanceledException"/> instead of completing.
    /// </summary>
    public bool DownloadCancels { get; set; }

    /// <summary>
    /// Gets or sets a callback invoked while the download is still in flight, so a test can
    /// observe state that only exists during it.
    /// </summary>
    public Action? DuringDownload { get; set; }

    /// <summary>Gets or sets what <see cref="ApplyAndRestart"/> returns.</summary>
    public bool ApplyResult { get; set; } = true;

    /// <summary>Sets <see cref="Status"/> and raises <see cref="StatusChanged"/>.</summary>
    /// <param name="status">The status to report.</param>
    public void SetStatus(UpdateStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }

    /// <summary>
    /// Reports that the given version has been found and downloaded.
    /// </summary>
    /// <param name="availableVersion">The version now waiting to be installed.</param>
    /// <param name="currentVersion">The version said to be running.</param>
    public void ReportDownloaded(string availableVersion, string currentVersion = "1.0.0")
        => SetStatus(new UpdateStatus
        {
            IsSupported      = true,
            CurrentVersion   = currentVersion,
            AvailableVersion = availableVersion,
            IsDownloaded     = true,
        });

    /// <summary>
    /// Reports that the given version has been found but not downloaded, which is the state a
    /// check leaves behind.
    /// </summary>
    /// <param name="availableVersion">The version that was found.</param>
    /// <param name="currentVersion">The version said to be running.</param>
    public void ReportAvailable(string availableVersion, string currentVersion = "1.0.0")
        => SetStatus(new UpdateStatus
        {
            IsSupported      = true,
            CurrentVersion   = currentVersion,
            AvailableVersion = availableVersion,
        });

    /// <summary>Reports that the given version is downloading right now.</summary>
    /// <param name="availableVersion">The version being downloaded.</param>
    /// <param name="currentVersion">The version said to be running.</param>
    public void ReportDownloading(string availableVersion, string currentVersion = "1.0.0")
        => SetStatus(new UpdateStatus
        {
            IsSupported      = true,
            CurrentVersion   = currentVersion,
            AvailableVersion = availableVersion,
            IsDownloading    = true,
        });

    /// <inheritdoc/>
    public Task<UpdateStatus> CheckAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Status);

    /// <inheritdoc/>
    public Task<bool> DownloadAsync(
        IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        DownloadCount++;

        foreach (var step in DownloadProgressSteps)
            progress?.Report(step);

        DuringDownload?.Invoke();

        if (DownloadCancels)
            throw new OperationCanceledException();

        if (DownloadResult)
            ReportDownloaded(Status.AvailableVersion ?? "0.0.0", Status.CurrentVersion ?? "0.0.0");

        return Task.FromResult(DownloadResult);
    }

    /// <inheritdoc/>
    public bool ApplyAndRestart()
    {
        ApplyCount++;
        return ApplyResult;
    }
}
