using Avalonia.Controls;

namespace ClipStudio.UI.Views;

/// <summary>
/// Code-behind for the first-run setup wizard window.
/// The window is shown instead of <see cref="MainWindow"/> when <c>AppSettings.IsFirstRun</c>
/// is <see langword="true"/>. All navigation logic lives in <see cref="ViewModels.SetupWizardViewModel"/>.
/// </summary>
public partial class SetupWizardWindow : Window
{
    /// <summary>Initialises a new <see cref="SetupWizardWindow"/>.</summary>
    public SetupWizardWindow()
    {
        InitializeComponent();
    }
}
