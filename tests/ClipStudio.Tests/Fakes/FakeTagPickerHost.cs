using ClipStudio.Core.Entities;
using ClipStudio.UI.Behaviors;

namespace ClipStudio.Tests.Fakes;

/// <summary>
/// Stands in for a tag <c>AutoCompleteBox</c> so <see cref="TagPickerStateMachine"/> can be driven
/// without an Avalonia control.
/// </summary>
public sealed class FakeTagPickerHost : ITagPickerHost
{
    /// <inheritdoc/>
    public Tag? SelectedTag { get; set; }

    /// <inheritdoc/>
    public string? Text { get; set; }

    /// <summary>Gets or sets the tag <see cref="ResolveTag"/> returns for the typed text.</summary>
    public Tag? TextResolution { get; set; }

    /// <summary>Gets the tags that were committed, in order.</summary>
    public List<Tag> Committed { get; } = [];

    /// <summary>Gets the number of times focus was returned to the picker.</summary>
    public int FocusCalls { get; private set; }

    /// <inheritdoc/>
    public Tag? ResolveTag() => SelectedTag ?? TextResolution;

    /// <inheritdoc/>
    public void Commit(Tag tag) => Committed.Add(tag);

    /// <inheritdoc/>
    public void Focus() => FocusCalls++;
}
