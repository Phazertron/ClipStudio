namespace ClipStudio.UI.ViewModels;

/// <summary>
/// The lifecycle states of a background task.
/// </summary>
public enum BackgroundTaskState
{
    /// <summary>The task is still working.</summary>
    Running = 0,

    /// <summary>The task finished and did what it set out to do.</summary>
    Completed = 1,

    /// <summary>The task stopped because the user cancelled it.</summary>
    Cancelled = 2,

    /// <summary>The task stopped because something went wrong.</summary>
    Failed = 3,
}
