using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ClipStudio.UI.ViewModels;

namespace ClipStudio.UI.Views;

/// <summary>
/// Code-behind for the Relocate Clip dialog.
/// Provides Browse, Confirm, and Cancel button handlers.
/// </summary>
public partial class RelocateClipDialog : Window
{
    private static readonly IReadOnlyList<FilePickerFileType> VideoFileFilter =
    [
        new FilePickerFileType("Video files")
        {
            Patterns = ["*.mp4", "*.mkv", "*.avi", "*.mov", "*.wmv", "*.flv", "*.webm", "*.ts", "*.m4v", "*.mpg", "*.mpeg"]
        },
        FilePickerFileTypes.All
    ];

    private RelocateClipDialogViewModel? Vm => DataContext as RelocateClipDialogViewModel;

    /// <summary>Initialises a new <see cref="RelocateClipDialog"/> (required by the AXAML loader).</summary>
    public RelocateClipDialog()
    {
        InitializeComponent();
    }

    /// <summary>Initialises a new <see cref="RelocateClipDialog"/> with a pre-built view model.</summary>
    public RelocateClipDialog(RelocateClipDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.Confirmed += () => Close(true);
        viewModel.Cancelled += () => Close(false);
    }

    private async void OnBrowseClicked(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title         = "Locate clip file",
            AllowMultiple = false,
            FileTypeFilter = VideoFileFilter
        });

        if (files.Count == 1)
        {
            var path = files[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
                Vm?.SetBrowsedPath(path);
        }
    }

    private void OnConfirmClicked(object? sender, RoutedEventArgs e) => Vm?.Confirm();

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Vm?.Cancel();
}
