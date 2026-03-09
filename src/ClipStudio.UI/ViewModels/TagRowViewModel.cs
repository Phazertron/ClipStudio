using System;
using System.Threading.Tasks;
using Avalonia;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model projection for a single <see cref="Tag"/> row in the tag manager list.
/// Exposes edit and delete commands that delegate back to the parent
/// <see cref="TagManagerViewModel"/> via constructor callbacks.
/// Supports hierarchical display via <see cref="IndentLevel"/>.
/// </summary>
public sealed class TagRowViewModel : ViewModelBase
{
    /// <summary>Gets the unique identifier of the underlying tag.</summary>
    public int TagId { get; }

    /// <summary>Gets the display name of the tag.</summary>
    public string Name { get; }

    /// <summary>Gets the hex colour string (e.g. "#E74C3C") for the colour swatch.</summary>
    public string Color { get; }

    /// <summary>Gets the optional description text.</summary>
    public string? Description { get; }

    /// <summary>Gets the semantic type of the tag.</summary>
    public TagType Type { get; }

    /// <summary>Gets the human-readable type label shown in the Type column.</summary>
    public string TypeLabel => Type == TagType.Game ? "Game" : "General";

    /// <summary>Gets a value indicating whether this tag is linked to a game store entry.</summary>
    public bool HasGameLink { get; }

    /// <summary>Gets the number of clips that carry this tag.</summary>
    public int ClipCount { get; }

    /// <summary>Gets the nesting depth of this tag in the hierarchy (0 = root).</summary>
    public int IndentLevel { get; }

    /// <summary>Gets a value indicating whether this tag is a child of another tag.</summary>
    public bool IsChild => IndentLevel > 0;

    /// <summary>Gets the left-margin thickness used to visually indent child tags in the list.</summary>
    public Thickness IndentMargin => new(IndentLevel * 20, 0, 0, 4);

    /// <summary>Gets the command that requests the parent VM to open an edit form for this tag.</summary>
    public IRelayCommand EditCommand { get; }

    /// <summary>Gets the command that deletes this tag after delegating to the parent VM.</summary>
    public IAsyncRelayCommand DeleteCommand { get; }

    /// <summary>
    /// Initialises a new <see cref="TagRowViewModel"/> from an entity and parent callbacks.
    /// </summary>
    /// <param name="tag">The tag entity to project.</param>
    /// <param name="onEdit">Callback invoked when the user clicks Edit.</param>
    /// <param name="onDelete">Callback invoked when the user confirms deletion.</param>
    /// <param name="indentLevel">Nesting depth in the tag hierarchy (0 for root tags).</param>
    public TagRowViewModel(Tag tag, Action<TagRowViewModel> onEdit, Func<TagRowViewModel, Task> onDelete,
                           int indentLevel = 0)
    {
        TagId       = tag.Id;
        Name        = tag.Name;
        Color       = tag.Color;
        Description = tag.Description;
        Type        = tag.Type;
        HasGameLink = tag.GameStoreAppId.HasValue;
        ClipCount   = tag.ClipTags.Count;
        IndentLevel = indentLevel;

        EditCommand   = new RelayCommand(() => onEdit(this));
        DeleteCommand = new AsyncRelayCommand(() => onDelete(this));
    }
}
