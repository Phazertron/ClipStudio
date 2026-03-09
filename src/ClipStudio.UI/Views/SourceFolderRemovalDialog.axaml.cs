using Avalonia.Controls;

namespace ClipStudio.UI.Views;

/// <summary>
/// Code-behind for the source folder removal confirmation dialog.
/// The <see cref="ViewModels.SourceFolderRemovalDialogViewModel.CloseRequested"/> callback is
/// wired by the caller immediately after construction (before
/// <see cref="Window.ShowDialog{TResult}"/> is invoked) to avoid a timing issue where
/// <c>AttachedToVisualTree</c> fires inside the Window constructor before the DataContext
/// object-initialiser assignment completes.
/// </summary>
public partial class SourceFolderRemovalDialog : Window
{
    /// <summary>Initialises a new <see cref="SourceFolderRemovalDialog"/>.</summary>
    public SourceFolderRemovalDialog()
    {
        InitializeComponent();
    }
}
