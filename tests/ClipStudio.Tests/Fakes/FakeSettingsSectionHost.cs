using ClipStudio.UI.ViewModels.Settings;

namespace ClipStudio.Tests.Fakes;

/// <summary>
/// Stands in for the Settings page so a <see cref="SettingsSectionViewModel"/> can be driven on
/// its own, without the page, a window or the rest of the sections.
/// </summary>
public sealed class FakeSettingsSectionHost : ISettingsSectionHost
{
    /// <inheritdoc/>
    public string? StatusMessage { get; set; }

    /// <summary>Gets the number of times a page reload was requested.</summary>
    public int ReloadCalls { get; private set; }

    /// <summary>Gets the number of times an unreviewed-count refresh was requested.</summary>
    public int UnreviewedCountRefreshCalls { get; private set; }

    /// <summary>Gets the number of times a finished repair was announced.</summary>
    public int LibraryRepairedCalls { get; private set; }

    /// <summary>Gets the repair-flag changes requested, in order.</summary>
    public List<bool> RepairingChanges { get; } = [];

    /// <summary>Gets whether a repair is currently marked as running.</summary>
    public bool IsRepairing => RepairingChanges.Count > 0 && RepairingChanges[^1];

    /// <inheritdoc/>
    public Task ReloadAsync()
    {
        ReloadCalls++;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void SetRepairing(bool repairing) => RepairingChanges.Add(repairing);

    /// <inheritdoc/>
    public void RequestUnreviewedCountRefresh() => UnreviewedCountRefreshCalls++;

    /// <inheritdoc/>
    public Task NotifyLibraryRepairedAsync()
    {
        LibraryRepairedCalls++;
        return Task.CompletedTask;
    }
}
