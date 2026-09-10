using System.Collections.ObjectModel;
using ClipStudio.UI.Services;
using ClipStudio.UI.ViewModels;
using Material.Icons;

namespace ClipStudio.Tests.Fakes;

/// <summary>
/// Stands in for the background task registry so a view model can be driven without a dispatcher.
/// </summary>
/// <remarks>
/// The real service marshals every mutation to the UI thread, which no test has. This keeps the
/// same shape and records what was started, so a test can assert that work announced itself and
/// finished the way it claimed to.
/// </remarks>
public sealed class FakeBackgroundTaskService : IBackgroundTaskService
{
    /// <inheritdoc/>
    public ObservableCollection<BackgroundTaskViewModel> Running { get; } = [];

    /// <inheritdoc/>
    public ObservableCollection<BackgroundTaskViewModel> Recent { get; } = [];

    /// <inheritdoc/>
    public int RunningCount => Running.Count;

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <summary>Gets every task ever started, running or not, in the order they started.</summary>
    public List<BackgroundTaskViewModel> Started { get; } = [];

    /// <summary>Gets the number of times the finished list was cleared.</summary>
    public int ClearFinishedCalls { get; private set; }

    /// <summary>Gets the only task started, failing the test when there is not exactly one.</summary>
    public BackgroundTaskViewModel Single => Started.Count == 1
        ? Started[0]
        : throw new InvalidOperationException($"Expected exactly one task, found {Started.Count}.");

    /// <inheritdoc/>
    public BackgroundTaskViewModel Start(
        string title,
        MaterialIconKind icon,
        string? ownerNavLabel = null,
        CancellationTokenSource? cancellation = null)
    {
        var task = new BackgroundTaskViewModel(title, icon, ownerNavLabel, cancellation, OnFinished);

        Started.Add(task);
        Running.Add(task);
        Changed?.Invoke(this, EventArgs.Empty);

        return task;
    }

    /// <inheritdoc/>
    public void ClearFinished()
    {
        ClearFinishedCalls++;
        Recent.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnFinished(BackgroundTaskViewModel task)
    {
        Running.Remove(task);
        Recent.Insert(0, task);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
