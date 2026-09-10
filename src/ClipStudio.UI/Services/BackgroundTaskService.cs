using System;
using System.Collections.ObjectModel;
using System.Threading;
using Avalonia.Threading;
using ClipStudio.UI.ViewModels;
using Material.Icons;

namespace ClipStudio.UI.Services;

/// <summary>
/// Keeps the list of running and recently finished background tasks.
/// </summary>
/// <remarks>
/// A singleton, because the whole point is that the list outlives the page that started the work.
/// Both collections are bound to directly, so every mutation is marshalled to the UI thread: a
/// scan reports progress from a worker, and Avalonia will not tolerate a collection change from
/// off-thread.
/// </remarks>
public sealed class BackgroundTaskService : IBackgroundTaskService
{
    /// <summary>How many finished tasks are kept before the oldest is dropped.</summary>
    /// <remarks>
    /// Enough to answer "what did that say again?" after the message has gone, without the panel
    /// turning into a log. The session is the lifetime; nothing is persisted.
    /// </remarks>
    private const int MaxRecent = 20;

    /// <inheritdoc/>
    public ObservableCollection<BackgroundTaskViewModel> Running { get; } = new();

    /// <inheritdoc/>
    public ObservableCollection<BackgroundTaskViewModel> Recent { get; } = new();

    /// <inheritdoc/>
    public int RunningCount => Running.Count;

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <inheritdoc/>
    public BackgroundTaskViewModel Start(
        string title,
        MaterialIconKind icon,
        string? ownerNavLabel = null,
        CancellationTokenSource? cancellation = null)
    {
        var task = new BackgroundTaskViewModel(title, icon, ownerNavLabel, cancellation, OnFinished);

        OnUiThread(() =>
        {
            Running.Add(task);
            Changed?.Invoke(this, EventArgs.Empty);
        });

        return task;
    }

    /// <inheritdoc/>
    public void ClearFinished() => OnUiThread(() =>
    {
        Recent.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    });

    /// <summary>Moves a task from the running list to the recent list.</summary>
    /// <param name="task">The task that finished.</param>
    private void OnFinished(BackgroundTaskViewModel task) => OnUiThread(() =>
    {
        Running.Remove(task);
        Recent.Insert(0, task);

        while (Recent.Count > MaxRecent)
            Recent.RemoveAt(Recent.Count - 1);

        Changed?.Invoke(this, EventArgs.Empty);
    });

    /// <summary>
    /// Runs an action on the UI thread, inline when already there.
    /// </summary>
    /// <param name="action">The action to run.</param>
    /// <remarks>
    /// Posting unconditionally would make <see cref="Start"/> return before the task was in the
    /// list, which a caller that immediately reports progress would notice.
    /// </remarks>
    private static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
