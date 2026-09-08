using System;
using System.Collections.Generic;
using ClipStudio.Core.Entities;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// The context <see cref="TrimEditorViewModel"/> needs from the clip detail view.
/// </summary>
/// <remarks>
/// Implemented by <see cref="ClipDetailViewModel"/>. The trim editor is not self-contained: it
/// marks against the current play head, measures its handles against the clip length, checks the
/// clip's highlights before a destructive trim, and shares the export queue with highlight export.
/// Routing all of that through one seam keeps those dependencies visible instead of scattered.
/// </remarks>
public interface ITrimEditorHost
{
    /// <summary>Gets the clip currently open, or <see langword="null"/> when there is none.</summary>
    Clip? CurrentClip { get; }

    /// <summary>
    /// Gets a value indicating whether trimming is offered at all. False in watch mode, where the
    /// view is showing one highlight rather than the whole clip.
    /// </summary>
    bool CanBeginTrim { get; }

    /// <summary>
    /// Closes anything that would clash with the trim form, so the timeline never shows two sets
    /// of handles at once.
    /// </summary>
    void PrepareForTrim();

    /// <summary>Gets the current play head in seconds, used when marking a trim point.</summary>
    double CurrentPositionSeconds { get; }

    /// <summary>Gets the length the timeline is showing, used to place the handles.</summary>
    double DurationSeconds { get; }

    /// <summary>Gets the highlights of the open clip, checked before a destructive trim.</summary>
    IReadOnlyList<HighlightViewModel> Highlights { get; }

    /// <summary>
    /// Signals that the trim form has opened or closed, so the timestamp readouts can switch
    /// between whole seconds and tenths.
    /// </summary>
    void OnTrimmingChanged();

    /// <summary>Starts draining the export queue, which is shared with highlight export.</summary>
    void RunExportQueue();

    /// <summary>Surfaces an export failure on the shared export status line.</summary>
    /// <param name="message">The message to show.</param>
    void ReportExportFailure(string message);
}
