using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// One piece of long-running work, as the activity panel shows it.
/// </summary>
/// <remarks>
/// Everything the user needs to know about a running operation lives here rather than in the
/// status line of whichever page happened to start it: what it is, what it is working on, how far
/// along it is, and what it said when it finished. A page keeps its own inline progress where that
/// is genuinely contextual - a folder row's scan bar belongs on the folder row - but the fact that
/// something is running has to be visible from anywhere.
/// </remarks>
public sealed partial class BackgroundTaskViewModel : ViewModelBase
{
    private readonly CancellationTokenSource? _cancellation;
    private readonly Action<BackgroundTaskViewModel>? _onFinished;

    /// <summary>Gets the title, naming what kind of work this is.</summary>
    public string Title { get; }

    /// <summary>Gets the icon shown beside the title.</summary>
    public MaterialIconKind Icon { get; }

    /// <summary>
    /// Gets the label of the navigation item this work belongs to, so its icon can spin while the
    /// task runs. Null when the work belongs to no particular page.
    /// </summary>
    public string? OwnerNavLabel { get; }

    /// <summary>Gets when the task started.</summary>
    public DateTime StartedAt { get; } = DateTime.Now;

    /// <summary>Gets or sets what the task is currently working on.</summary>
    [ObservableProperty]
    private string? _detail;

    /// <summary>Gets or sets how far along the task is, from 0 to 100.</summary>
    [ObservableProperty]
    private double _progressPercent;

    /// <summary>
    /// Gets or sets whether the task cannot say how far along it is.
    /// </summary>
    /// <remarks>
    /// True until the task reports a determinate percentage. A repair pass never can - it reports
    /// what it is doing rather than how much is left - and showing it as 0% forever would read as
    /// stuck rather than busy.
    /// </remarks>
    [ObservableProperty]
    private bool _isIndeterminate = true;

    /// <summary>Gets or sets the task's current state.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsFinished))]
    [NotifyPropertyChangedFor(nameof(StateDisplay))]
    private BackgroundTaskState _state = BackgroundTaskState.Running;

    /// <summary>Gets or sets what the task said when it finished.</summary>
    /// <remarks>
    /// Kept in full. This is the line that used to be truncated at the edge of the settings page:
    /// a sanitize summary is long because it has a lot to report, and cutting it loses the part
    /// that says whether anything needs attention.
    /// </remarks>
    [ObservableProperty]
    private string? _completionMessage;

    /// <summary>Gets whether the task is still working.</summary>
    public bool IsRunning => State == BackgroundTaskState.Running;

    /// <summary>Gets whether the task has stopped, for any reason.</summary>
    public bool IsFinished => State != BackgroundTaskState.Running;

    /// <summary>Gets a short description of how the task ended.</summary>
    public string StateDisplay => State switch
    {
        BackgroundTaskState.Completed => "Finished",
        BackgroundTaskState.Cancelled => "Cancelled",
        BackgroundTaskState.Failed    => "Failed",
        _                             => "Running",
    };

    /// <summary>Gets whether this task can be cancelled.</summary>
    public bool CanCancel => _cancellation is not null && IsRunning;

    /// <summary>Gets the command that asks the task to stop.</summary>
    public IRelayCommand CancelCommand { get; }

    /// <summary>Initialises a new <see cref="BackgroundTaskViewModel"/>.</summary>
    /// <param name="title">The title naming what kind of work this is.</param>
    /// <param name="icon">The icon shown beside the title.</param>
    /// <param name="ownerNavLabel">The navigation item the work belongs to, if any.</param>
    /// <param name="cancellation">
    /// The source to cancel through, when the work is cancellable. Null makes the task
    /// display-only, which is the honest state for work that cannot be interrupted safely.
    /// </param>
    /// <param name="onFinished">Invoked once when the task reaches a finished state.</param>
    public BackgroundTaskViewModel(
        string title,
        MaterialIconKind icon,
        string? ownerNavLabel = null,
        CancellationTokenSource? cancellation = null,
        Action<BackgroundTaskViewModel>? onFinished = null)
    {
        Title         = title;
        Icon          = icon;
        OwnerNavLabel = ownerNavLabel;
        _cancellation = cancellation;
        _onFinished   = onFinished;

        CancelCommand = new RelayCommand(Cancel, () => CanCancel);
    }

    /// <summary>Reports determinate progress.</summary>
    /// <param name="percent">How far along the task is, from 0 to 100.</param>
    /// <param name="detail">What the task is working on, if it changed.</param>
    public void Report(double percent, string? detail = null)
    {
        IsIndeterminate = false;
        ProgressPercent = Math.Clamp(percent, 0, 100);
        if (detail is not null) Detail = detail;
    }

    /// <summary>Reports that the task is busy without saying how far along it is.</summary>
    /// <param name="detail">What the task is working on.</param>
    public void Report(string detail) => Detail = detail;

    /// <summary>Marks the task finished.</summary>
    /// <param name="state">How it ended.</param>
    /// <param name="message">What to show as its outcome.</param>
    public void Finish(BackgroundTaskState state, string? message = null)
    {
        if (IsFinished) return;

        State             = state;
        CompletionMessage = message;
        Detail            = null;

        if (state == BackgroundTaskState.Completed)
        {
            IsIndeterminate = false;
            ProgressPercent = 100;
        }

        CancelCommand.NotifyCanExecuteChanged();
        _onFinished?.Invoke(this);
    }

    /// <summary>Asks the task to stop.</summary>
    /// <remarks>
    /// Only requests it. The task decides when it is safe to stop, and reports
    /// <see cref="BackgroundTaskState.Cancelled"/> itself once it has - so a task that has already
    /// passed its last cancellation point still finishes normally.
    /// </remarks>
    private void Cancel()
    {
        if (!CanCancel) return;

        Detail = "Stopping...";
        try
        {
            _cancellation!.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The task finished between the check and the cancel.
        }

        CancelCommand.NotifyCanExecuteChanged();
    }
}
