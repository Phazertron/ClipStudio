using System.Threading.Tasks;
using ClipStudio.Core.Entities;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// The context <see cref="HighlightEditorViewModel"/> needs from the clip detail view.
/// </summary>
/// <remarks>
/// Implemented by <see cref="ClipDetailViewModel"/>, which keeps the highlight <em>list</em> and
/// everything the list rows do. The editor owns only the range being written: the add form and the
/// inline edit of an existing row.
/// </remarks>
public interface IHighlightEditorHost
{
    /// <summary>Gets the clip currently open, or <see langword="null"/> when there is none.</summary>
    Clip? CurrentClip { get; }

    /// <summary>Gets the current play head in seconds, used when marking a highlight point.</summary>
    double CurrentPositionSeconds { get; }

    /// <summary>Gets the length the timeline is showing, used to place the handles.</summary>
    double DurationSeconds { get; }

    /// <summary>
    /// Closes anything that would clash with the highlight form, so the timeline never shows two
    /// sets of handles at once.
    /// </summary>
    void PrepareForHighlightEdit();

    /// <summary>
    /// Signals that the editor has opened, closed, or switched between adding and editing, so the
    /// timestamp readouts can switch between whole seconds and tenths.
    /// </summary>
    void OnEditingStateChanged();

    /// <summary>
    /// Signals that a highlight has been created, so the list can be rebuilt and the rest of the
    /// app told about it.
    /// </summary>
    /// <returns>A task that completes once the list has been refreshed.</returns>
    Task OnHighlightCreatedAsync();
}
