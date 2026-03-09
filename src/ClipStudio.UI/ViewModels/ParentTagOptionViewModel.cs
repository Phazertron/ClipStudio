namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Represents a single selectable option in the parent-tag dropdown of the tag editor form.
/// A null <see cref="TagId"/> represents the "No parent (root tag)" option.
/// </summary>
public sealed class ParentTagOptionViewModel : ViewModelBase
{
    /// <summary>Gets the tag identifier, or null for the root (no-parent) option.</summary>
    public int? TagId { get; }

    /// <summary>Gets the display label shown in the dropdown.</summary>
    public string DisplayName { get; }

    /// <summary>Initialises a new <see cref="ParentTagOptionViewModel"/>.</summary>
    /// <param name="tagId">Tag identifier, or null for the no-parent option.</param>
    /// <param name="displayName">Display label shown in the combo box.</param>
    public ParentTagOptionViewModel(int? tagId, string displayName)
    {
        TagId       = tagId;
        DisplayName = displayName;
    }
}
