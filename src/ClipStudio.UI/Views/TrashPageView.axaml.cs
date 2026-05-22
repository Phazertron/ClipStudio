using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ClipStudio.UI.ViewModels;

namespace ClipStudio.UI.Views;

/// <summary>
/// Code-behind for the Trash page view.
/// </summary>
public partial class TrashPageView : UserControl
{
    /// <summary>Initializes a new instance of <see cref="TrashPageView"/>.</summary>
    public TrashPageView()
    {
        InitializeComponent();
    }

    private async void OnRelocateClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control ctrl) return;
        if (ctrl.DataContext is not TrashedClipRowViewModel row) return;
        if (DataContext is not TrashPageViewModel trashVm) return;
        if (TopLevel.GetTopLevel(this) is not Window window) return;

        e.Handled = true;

        var sourcePaths = await trashVm.GetSourcePathsAsync();
        var dialogVm    = new RelocateClipDialogViewModel(row.FileName, row.OriginalPath, sourcePaths);
        var dialog      = new RelocateClipDialog(dialogVm);
        var confirmed   = await dialog.ShowDialog<bool>(window);
        if (!confirmed) return;

        if (dialogVm.Mode == RelocateMode.MoveBack)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(dialogVm.OriginalPath);
                if (!string.IsNullOrEmpty(dir))
                    System.IO.Directory.CreateDirectory(dir);
                System.IO.File.Move(dialogVm.SelectedFilePath, dialogVm.OriginalPath, overwrite: false);
            }
            catch (Exception ex)
            {
                trashVm.StatusMessage = $"Could not move file: {ex.Message}";
                return;
            }
            await trashVm.RelocateClipAsync(row.ClipId, dialogVm.OriginalPath, null);
        }
        else
        {
            var addSource = dialogVm.WillAddNewSource ? dialogVm.NewFolderPath : null;
            await trashVm.RelocateClipAsync(row.ClipId, dialogVm.SelectedFilePath, addSource);
        }
    }
}
