using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for a removable tag chip shown in the clip detail side panel.
/// Wraps a single tag assignment and exposes a remove command that delegates
/// back to the parent view model via a constructor callback.
/// Tags that are only present on the clip because they are propagated from a highlight
/// are marked with <see cref="IsPropagated"/> and cannot be removed directly.
/// </summary>
public sealed class TagChipViewModel : ViewModelBase
{
    /// <summary>Gets the database identifier of the tag.</summary>
    public int TagId { get; }

    /// <summary>Gets the display name of the tag.</summary>
    public string Name { get; }

    /// <summary>
    /// Gets a value indicating whether this tag is present on the clip solely because of
    /// highlight propagation (i.e. it was not directly added to the clip itself).
    /// Propagated tags have a muted visual style and cannot be removed from the clip directly.
    /// </summary>
    public bool IsPropagated { get; }

    /// <summary>Gets the command that removes this tag from its parent (clip or highlight).</summary>
    public IAsyncRelayCommand RemoveCommand { get; }

    /// <summary>
    /// Initialises a new <see cref="TagChipViewModel"/>.
    /// </summary>
    /// <param name="tagId">The database identifier of the tag.</param>
    /// <param name="name">The display name of the tag.</param>
    /// <param name="onRemove">Async callback invoked when the user removes this chip.</param>
    /// <param name="isPropagated">
    /// When <see langword="true"/> the chip is rendered in a muted style and the
    /// <paramref name="onRemove"/> callback is not invoked on remove.
    /// </param>
    public TagChipViewModel(int tagId, string name, Func<TagChipViewModel, Task> onRemove, bool isPropagated = false)
    {
        TagId         = tagId;
        Name          = name;
        IsPropagated  = isPropagated;
        RemoveCommand = new AsyncRelayCommand(() =>
        {
            if (isPropagated) return Task.CompletedTask;
            return onRemove(this);
        });
    }
}
