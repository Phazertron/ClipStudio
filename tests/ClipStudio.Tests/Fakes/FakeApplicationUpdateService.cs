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

    /// <inheritdoc/>
    public Task<UpdateStatus> CheckAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Status);

    /// <inheritdoc/>
    public bool ApplyAndRestart()
    {
        ApplyCount++;
        return ApplyResult;
    }
}
