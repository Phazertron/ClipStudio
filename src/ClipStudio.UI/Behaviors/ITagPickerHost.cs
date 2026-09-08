using ClipStudio.Core.Entities;

namespace ClipStudio.UI.Behaviors;

/// <summary>
/// The seam between <see cref="TagPickerStateMachine"/> and the control it drives.
/// Keeps the state machine free of Avalonia so its transitions can be tested with a fake host.
/// </summary>
public interface ITagPickerHost
{
    /// <summary>Gets or sets the tag currently selected in the picker, or <c>null</c> when none is.</summary>
    Tag? SelectedTag { get; set; }

    /// <summary>Gets or sets the text currently typed into the picker.</summary>
    string? Text { get; set; }

    /// <summary>
    /// Resolves the tag that should be committed. Implementations check the current selection
    /// first and fall back to matching <see cref="Text"/> against the available tags.
    /// </summary>
    /// <returns>The resolved tag, or <c>null</c> when nothing matches.</returns>
    Tag? ResolveTag();

    /// <summary>Applies a resolved tag to whatever the picker is editing.</summary>
    /// <param name="tag">The tag to apply.</param>
    void Commit(Tag tag);

    /// <summary>Returns keyboard focus to the picker so the user can type the next tag.</summary>
    void Focus();
}
