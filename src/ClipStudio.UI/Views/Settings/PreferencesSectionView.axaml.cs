using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ClipStudio.UI.ViewModels.Settings;

namespace ClipStudio.UI.Views.Settings;

/// <summary>
/// Code-behind for the Preferences settings section.
/// Owns the one interaction that needs a window: the screenshot output folder picker.
/// </summary>
public partial class PreferencesSectionView : UserControl
{
    /// <summary>Initialises a new <see cref="PreferencesSectionView"/>.</summary>
    public PreferencesSectionView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Opens a folder picker so the user can choose the screenshot output folder.
    /// </summary>
    private async void OnBrowseScreenshotFolderClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var results = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Select Screenshot Output Folder", AllowMultiple = false });

        if (results.Count > 0 && DataContext is PreferencesSectionViewModel vm)
            vm.ScreenshotOutputFolder = results[0].Path.LocalPath;
    }
}
