using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ClipStudio.UI.ViewModels;
using ClipStudio.UI.ViewModels.Settings;

namespace ClipStudio.UI.Views.Settings;

/// <summary>
/// Code-behind for the Source Folders settings section.
/// </summary>
/// <remarks>
/// Owns every interaction that needs a window: the folder picker, the removal dialog and the
/// duplicate prompt. The section view model stays free of windows and is supplied the duplicate
/// prompt through a delegate, which is how it remains testable.
/// </remarks>
public partial class SourceFoldersSectionView : UserControl
{
    /// <summary>Initialises a new <see cref="SourceFoldersSectionView"/>.</summary>
    public SourceFoldersSectionView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is SourceFoldersSectionViewModel vm)
            vm.DuplicateResolutionRequested = AskAboutDuplicateAsync;
    }

    /// <summary>
    /// Shows the duplicate dialog and returns what the user chose.
    /// </summary>
    /// <param name="prompt">The duplicate being asked about.</param>
    /// <returns>
    /// The user's resolution, or a skip when there is no window to show a dialog over - dismissing
    /// the dialog must never import something by default.
    /// </returns>
    private async Task<DuplicateResolution> AskAboutDuplicateAsync(DuplicateClipPrompt prompt)
    {
        if (TopLevel.GetTopLevel(this) is not Window window)
            return DuplicateResolution.Skip;

        var dialogVm = new DuplicateClipDialogViewModel(prompt);
        var dialog   = new DuplicateClipDialog { DataContext = dialogVm };
        dialogVm.CloseRequested = resolution => dialog.Close(resolution);

        // Closing the window with its title bar returns null; treat that as a skip.
        return await dialog.ShowDialog<DuplicateResolution?>(window) ?? DuplicateResolution.Skip;
    }

    /// <summary>
    /// Opens a folder picker so the user can choose a new source folder to watch.
    /// </summary>
    private async void OnBrowseSourceFolderClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var results = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Select Source Folder to Watch", AllowMultiple = false });

        if (results.Count > 0 && DataContext is SourceFoldersSectionViewModel vm)
            vm.NewFolderPath = results[0].Path.LocalPath;
    }

    /// <summary>
    /// Handles the Remove button click on a source folder row.
    /// Shows the <see cref="SourceFolderRemovalDialog"/> so the user can choose Archive, Wipe, or
    /// Cancel, then delegates to the matching section view model method.
    /// </summary>
    private async void OnRemoveFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.DataContext is not SourceFolderRowViewModel row) return;
        if (DataContext is not SourceFoldersSectionViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window window) return;

        var dialogVm = new SourceFolderRemovalDialogViewModel(row.Path);
        var dialog   = new SourceFolderRemovalDialog { DataContext = dialogVm };
        dialogVm.CloseRequested = r => dialog.Close(r);
        var result   = await dialog.ShowDialog<SourceFolderRemovalResult>(window);

        switch (result)
        {
            case SourceFolderRemovalResult.Archive:
                await vm.ArchiveFolderAsync(row);
                break;
            case SourceFolderRemovalResult.Wipe:
                await vm.WipeFolderAsync(row);
                break;
            // Cancel: do nothing.
        }
    }
}
