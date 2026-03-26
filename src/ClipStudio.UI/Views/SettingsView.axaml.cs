using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ClipStudio.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ClipStudio.UI.Views;

/// <summary>
/// Code-behind for <see cref="SettingsView"/>.
/// Triggers an initial data load when the view is attached to the visual tree and
/// handles folder/clipboard interactions that require a reference to the top-level window.
/// </summary>
public partial class SettingsView : UserControl
{
    /// <summary>Initialises a new <see cref="SettingsView"/> and wires up component events.</summary>
    public SettingsView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DataContextChanged   += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            vm.ManageModelsRequested += OnManageModelsRequested;
    }

    private async void OnManageModelsRequested()
    {
        if (DataContext is not SettingsViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window window) return;

        var settings = App.Services.GetRequiredService<ClipStudio.Application.Interfaces.ISettingsService>();
        var dlgVm    = new TranscriptionSetupDialogViewModel(settings);
        dlgVm.Confirmed += async () => await vm.LoadAsync();
        var dialog = new TranscriptionSetupDialog(dlgVm);
        await dialog.ShowDialog(window);
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            vm.LoadCommand.Execute(null);
    }

    /// <summary>
    /// Opens a folder picker so the user can choose a new source folder to watch.
    /// Updates <see cref="SettingsViewModel.NewFolderPath"/> on confirmation.
    /// </summary>
    private async void OnBrowseSourceFolderClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var results = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Select Source Folder to Watch", AllowMultiple = false });

        if (results.Count > 0 && DataContext is SettingsViewModel vm)
            vm.NewFolderPath = results[0].Path.LocalPath;
    }

    /// <summary>
    /// Opens a folder picker so the user can choose the screenshot output folder.
    /// Updates <see cref="SettingsViewModel.ScreenshotOutputFolder"/> on confirmation.
    /// </summary>
    private async void OnBrowseScreenshotFolderClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var results = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Select Screenshot Output Folder", AllowMultiple = false });

        if (results.Count > 0 && DataContext is SettingsViewModel vm)
            vm.ScreenshotOutputFolder = results[0].Path.LocalPath;
    }

    /// <summary>
    /// Copies the OBS script path to the system clipboard.
    /// </summary>
    private async void OnCopyOBSScriptPathClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null) return;

        if (DataContext is SettingsViewModel vm)
            await topLevel.Clipboard.SetTextAsync(vm.OBSScriptPath);
    }

    /// <summary>
    /// Handles the Remove button click on a source folder row.
    /// Shows the <see cref="SourceFolderRemovalDialog"/> so the user can choose Archive, Wipe, or Cancel,
    /// then delegates to the appropriate <see cref="SettingsViewModel"/> method.
    /// </summary>
    private async void OnBrowseTranscriptionSrtFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;
        var folder = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title         = "Select SRT output folder",
            AllowMultiple = false
        });
        if (folder.Count > 0)
        {
            var path = folder[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
                vm.TranscriptionSrtFolder = path;
        }
    }

    private async void OnRemoveFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.DataContext is not SourceFolderRowViewModel row) return;
        if (DataContext is not SettingsViewModel vm) return;
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
            // Cancel: do nothing
        }
    }
}
