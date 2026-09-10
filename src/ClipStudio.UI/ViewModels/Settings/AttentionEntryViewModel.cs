using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace ClipStudio.UI.ViewModels.Settings;

/// <summary>
/// One row in the attention list: what is wrong, and the one thing that fixes it.
/// </summary>
/// <remarks>
/// An entry describes a problem and offers an action; it never takes the action on its own. That
/// is the rule for this whole section - the sanitizer reports an out-of-range highlight rather
/// than moving it because there is no correct range to guess, and the same standard applies to
/// every other entry here.
/// </remarks>
public sealed class AttentionEntryViewModel : ViewModelBase
{
    /// <summary>Gets what kind of problem this entry describes.</summary>
    public AttentionEntryKind Kind { get; }

    /// <summary>Gets the one-line headline, naming the thing that is wrong.</summary>
    public string Title { get; }

    /// <summary>Gets the supporting detail: which file, which folder, how many.</summary>
    public string Detail { get; }

    /// <summary>Gets the icon shown beside the entry.</summary>
    public MaterialIconKind Icon { get; }

    /// <summary>Gets the label of the offered action, or null when the entry only reports.</summary>
    public string? ActionLabel { get; }

    /// <summary>Gets the offered action, or null when the entry only reports.</summary>
    public IAsyncRelayCommand? ActionCommand { get; }

    /// <summary>Gets whether this entry offers an action at all.</summary>
    public bool HasAction => ActionCommand is not null;

    /// <summary>Initialises a new <see cref="AttentionEntryViewModel"/>.</summary>
    /// <param name="kind">What kind of problem this entry describes.</param>
    /// <param name="title">The headline.</param>
    /// <param name="detail">The supporting detail.</param>
    /// <param name="icon">The icon shown beside the entry.</param>
    /// <param name="actionLabel">The label of the offered action, if any.</param>
    /// <param name="action">The offered action, if any.</param>
    public AttentionEntryViewModel(
        AttentionEntryKind kind,
        string title,
        string detail,
        MaterialIconKind icon,
        string? actionLabel = null,
        Func<Task>? action = null)
    {
        Kind        = kind;
        Title       = title;
        Detail      = detail;
        Icon        = icon;
        ActionLabel = action is null ? null : actionLabel;

        if (action is not null)
            ActionCommand = new AsyncRelayCommand(action);
    }
}
