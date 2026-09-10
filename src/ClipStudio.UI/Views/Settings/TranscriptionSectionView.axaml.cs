using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ClipStudio.UI.ViewModels.Settings;

namespace ClipStudio.UI.Views.Settings;

/// <summary>
/// Code-behind for the Transcription settings section.
/// Owns the model management dialog and the SRT folder picker, both of which need a window.
/// </summary>
public partial class TranscriptionSectionView : UserControl
{
    /// <summary>Initialises a new <see cref="TranscriptionSectionView"/>.</summary>
    public TranscriptionSectionView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Opens the Whisper model management dialog and reloads the page once it confirms.
    /// </summary>
    private async void OnManageModelsClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TranscriptionSectionViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window window) return;

        try
        {
            var dialogVm = new TranscriptionSetupDialogViewModel(vm.SettingsService);
            dialogVm.Confirmed += async () => await vm.ReloadPageAsync();
            var dialog = new TranscriptionSetupDialog(dialogVm);
            await dialog.ShowDialog(window);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(window, ex);
        }
    }

    /// <summary>
    /// Opens a folder picker so the user can choose where generated SRT files are written.
    /// </summary>
    private async void OnBrowseTranscriptionSrtFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TranscriptionSectionViewModel vm) return;

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

    /// <summary>
    /// Shows an unhandled dialog failure in a scrollable window rather than losing it.
    /// </summary>
    /// <param name="owner">The window to show the error over.</param>
    /// <param name="ex">The exception to display.</param>
    private static async System.Threading.Tasks.Task ShowErrorAsync(Window owner, Exception ex)
    {
        await new Window
        {
            Title   = "Error",
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text         = ex.ToString(),
                    Margin       = new Avalonia.Thickness(16),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize     = 11
                }
            },
            Width  = 600,
            Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        }.ShowDialog(owner);
    }
}
