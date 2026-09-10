using Avalonia.Controls;
using ClipStudio.UI.ViewModels;

namespace ClipStudio.UI.Views;

/// <summary>
/// Code-behind for <see cref="SettingsView"/>.
/// </summary>
/// <remarks>
/// The page is now only a shell around the section views, so everything that needs a window - the
/// folder pickers, the removal dialog, the duplicate prompt, the model manager - moved into the
/// code-behind of the section it belongs to. All that is left here is the initial load.
/// </remarks>
public partial class SettingsView : UserControl
{
    /// <summary>Initialises a new <see cref="SettingsView"/> and wires up component events.</summary>
    public SettingsView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            vm.LoadCommand.Execute(null);
    }
}
