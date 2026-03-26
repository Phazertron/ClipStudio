using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace ClipStudio.UI.Views;

/// <summary>
/// Code-behind for <see cref="TranscriptionSetupDialog"/>.
/// Wires Browse and OK/Cancel button clicks to the view model and provides
/// the <see cref="SelectedBackground"/> converter used in the model row template.
/// </summary>
public partial class TranscriptionSetupDialog : Window
{
    /// <summary>
    /// Converter that returns a subtle highlight background when a model row is selected.
    /// Used in the AXAML DataTemplate to highlight the active row.
    /// </summary>
    public static readonly IValueConverter SelectedBackground =
        new FuncValueConverter<bool, IBrush?>(
            isSelected => isSelected ? new SolidColorBrush(Color.FromArgb(40, 100, 100, 255)) : null);

    private TranscriptionSetupDialogViewModel? Vm => DataContext as TranscriptionSetupDialogViewModel;

    /// <summary>Initialises a new <see cref="TranscriptionSetupDialog"/> (required by Avalonia AXAML loader).</summary>
    public TranscriptionSetupDialog()
    {
        InitializeComponent();
    }

    /// <summary>Initialises a new <see cref="TranscriptionSetupDialog"/> with a pre-built view model.</summary>
    /// <param name="viewModel">The view model to bind to this dialog.</param>
    public TranscriptionSetupDialog(TranscriptionSetupDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.Confirmed += () => Close(true);
        viewModel.Cancelled += () => Close(false);
    }

    private async void OnBrowseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title            = "Select Whisper model (.bin)",
            AllowMultiple    = false,
            FileTypeFilter   = new[]
            {
                new FilePickerFileType("GGML model") { Patterns = new[] { "*.bin" } },
                new FilePickerFileType("All files")  { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
                Vm?.SetBrowsedPath(path);
        }
    }

    private async void OnOkClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Vm is not null)
            await Vm.ConfirmAsync();
    }

    private void OnCancelClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Vm?.Cancel();
    }
}
