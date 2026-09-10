using Avalonia.Controls;
using Avalonia.Interactivity;
using ClipStudio.UI.ViewModels.Settings;

namespace ClipStudio.UI.Views.Settings;

/// <summary>
/// Code-behind for the OBS Integration settings section.
/// Owns the clipboard access, which needs the top-level window.
/// </summary>
public partial class ObsIntegrationSectionView : UserControl
{
    /// <summary>Initialises a new <see cref="ObsIntegrationSectionView"/>.</summary>
    public ObsIntegrationSectionView()
    {
        InitializeComponent();
    }

    /// <summary>Copies the OBS script path to the system clipboard.</summary>
    private async void OnCopyObsScriptPathClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null) return;

        if (DataContext is ObsIntegrationSectionViewModel vm)
            await topLevel.Clipboard.SetTextAsync(vm.ObsScriptPath);
    }
}
