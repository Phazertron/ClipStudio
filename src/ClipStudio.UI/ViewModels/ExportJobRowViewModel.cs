using System;
using System.Threading.Tasks;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Read-only view model projection for a single <see cref="ExportJob"/> row in the export queue page.
/// Exposes a <see cref="CancelCommand"/> that delegates to the parent queue's cancel logic.
/// </summary>
public sealed class ExportJobRowViewModel : ViewModelBase
{
    /// <summary>Gets the unique identifier of the export job.</summary>
    public int JobId { get; }

    /// <summary>Gets the file name of the clip being exported.</summary>
    public string ClipName { get; }

    /// <summary>Gets the label of the highlight being exported, or "Full Clip" if none.</summary>
    public string HighlightLabel { get; }

    /// <summary>Gets the absolute output path of the exported file.</summary>
    public string OutputPath { get; }

    /// <summary>Gets the current status of the job.</summary>
    public ExportJobStatus Status { get; }

    /// <summary>Gets the human-readable status label.</summary>
    public string StatusLabel => Status.ToString();

    /// <summary>Gets the trim mode label.</summary>
    public string TrimModeLabel { get; }

    /// <summary>Gets a value indicating whether the job uses destructive (re-encode) mode.</summary>
    public bool IsDestructive { get; }

    /// <summary>Gets the creation timestamp formatted for display.</summary>
    public string CreatedAtDisplay { get; }

    /// <summary>Gets the completion timestamp formatted for display, or a dash if not yet completed.</summary>
    public string CompletedAtDisplay { get; }

    /// <summary>Gets the error message if the job failed, otherwise null.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Gets a value indicating whether the job is currently pending or processing.</summary>
    public bool IsActive => Status is ExportJobStatus.Pending or ExportJobStatus.Processing;

    /// <summary>Gets a value indicating whether the job is pending (and therefore can be cancelled).</summary>
    public bool IsPending => Status == ExportJobStatus.Pending;

    /// <summary>Gets a value indicating whether the job completed successfully.</summary>
    public bool IsCompleted => Status == ExportJobStatus.Completed;

    /// <summary>Gets a value indicating whether the job failed.</summary>
    public bool IsFailed => Status == ExportJobStatus.Failed;

    /// <summary>Gets a value indicating whether the job was cancelled.</summary>
    public bool IsCancelled => Status == ExportJobStatus.Cancelled;

    /// <summary>Gets the command that cancels this pending job.</summary>
    public IAsyncRelayCommand CancelCommand { get; }

    /// <summary>
    /// Initialises a new <see cref="ExportJobRowViewModel"/> from an <see cref="ExportJob"/> entity.
    /// </summary>
    /// <param name="job">The export job entity to project.</param>
    /// <param name="onCancel">
    /// Async action invoked when <see cref="CancelCommand"/> is executed.
    /// Pass null for completed/failed rows where cancellation is not applicable.
    /// </param>
    public ExportJobRowViewModel(ExportJob job, Func<Task>? onCancel = null)
    {
        JobId          = job.Id;
        ClipName       = System.IO.Path.GetFileName(job.Clip?.FilePath ?? $"Clip {job.ClipId}");
        HighlightLabel = job.Highlight?.Label is { Length: > 0 } label ? label : "Full Clip";
        OutputPath     = job.OutputPath;
        Status         = job.Status;
        TrimModeLabel  = job.TrimMode == TrimMode.Destructive ? "Destructive" : "Non-Destructive";
        IsDestructive  = job.TrimMode == TrimMode.Destructive;
        CreatedAtDisplay   = job.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        CompletedAtDisplay = job.CompletedAt.HasValue
            ? job.CompletedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "-";
        ErrorMessage = job.ErrorMessage;
        CancelCommand = new AsyncRelayCommand(() => onCancel?.Invoke() ?? Task.CompletedTask);
    }
}
