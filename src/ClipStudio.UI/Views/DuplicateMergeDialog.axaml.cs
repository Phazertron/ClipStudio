using Avalonia.Controls;

namespace ClipStudio.UI.Views;

/// <summary>
/// Dialog that resolves one group of duplicate clips.
/// </summary>
/// <remarks>
/// Owns nothing but the window: the view model closes it through its
/// <see cref="ViewModels.DuplicateMergeViewModel.CloseRequested"/> callback, reporting whether the
/// merge was applied so the caller can refresh.
/// </remarks>
public partial class DuplicateMergeDialog : Window
{
    /// <summary>Initialises a new <see cref="DuplicateMergeDialog"/>.</summary>
    public DuplicateMergeDialog()
    {
        InitializeComponent();
    }
}
