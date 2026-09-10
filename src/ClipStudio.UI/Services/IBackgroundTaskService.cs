using System;
using System.Collections.ObjectModel;
using System.Threading;
using ClipStudio.UI.ViewModels;
using Material.Icons;

namespace ClipStudio.UI.Services;

/// <summary>
/// The one place that knows what long-running work is in flight.
/// </summary>
/// <remarks>
/// Before this, each operation reported into whichever page started it - a folder row's scan bar,
/// the settings status line, a transcription panel - so nothing running was visible from anywhere
/// else, and a finished summary was a single string on a one-line bar that ran off the edge of the
/// window. Every long-running operation now registers here, and the activity indicator, the
/// navigation spinner and the task panel all read from it.
/// <para>
/// Pages keep their own inline progress where it is genuinely contextual. This is about the fact
/// that something is running being visible from anywhere, not about moving every progress bar.
/// </para>
/// </remarks>
public interface IBackgroundTaskService
{
    /// <summary>Gets the tasks currently running, oldest first.</summary>
    ObservableCollection<BackgroundTaskViewModel> Running { get; }

    /// <summary>Gets the tasks that recently finished, newest first.</summary>
    ObservableCollection<BackgroundTaskViewModel> Recent { get; }

    /// <summary>Gets how many tasks are running.</summary>
    int RunningCount { get; }

    /// <summary>Raised whenever a task starts or finishes, so the chrome can follow.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Registers a new task and marks it running.
    /// </summary>
    /// <param name="title">The title naming what kind of work this is.</param>
    /// <param name="icon">The icon shown beside the title.</param>
    /// <param name="ownerNavLabel">
    /// The navigation item the work belongs to, so its icon can spin while the task runs.
    /// </param>
    /// <param name="cancellation">
    /// The source to cancel through, when the work is cancellable. Null makes the task
    /// display-only, which is the honest state for work that cannot be interrupted safely.
    /// </param>
    /// <returns>The task, to report progress into and to finish.</returns>
    BackgroundTaskViewModel Start(
        string title,
        MaterialIconKind icon,
        string? ownerNavLabel = null,
        CancellationTokenSource? cancellation = null);

    /// <summary>Forgets every finished task.</summary>
    void ClearFinished();
}
