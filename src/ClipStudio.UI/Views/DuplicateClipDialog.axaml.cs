using Avalonia.Controls;

namespace ClipStudio.UI.Views;

/// <summary>
/// Code-behind for the duplicate clip dialog.
/// </summary>
/// <remarks>
/// The <see cref="ViewModels.DuplicateClipDialogViewModel.CloseRequested"/> callback is wired by
/// the caller immediately after construction, before <see cref="Window.ShowDialog{TResult}"/> is
/// invoked, matching <see cref="SourceFolderRemovalDialog"/>.
/// </remarks>
public partial class DuplicateClipDialog : Window
{
    /// <summary>Initialises a new <see cref="DuplicateClipDialog"/>.</summary>
    public DuplicateClipDialog()
    {
        InitializeComponent();
    }
}
