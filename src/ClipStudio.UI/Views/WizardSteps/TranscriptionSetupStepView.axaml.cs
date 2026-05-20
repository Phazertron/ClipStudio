using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ClipStudio.UI.ViewModels.WizardSteps;

namespace ClipStudio.UI.Views.WizardSteps;

/// <summary>
/// Code-behind for <see cref="TranscriptionSetupStepView"/>.
/// Subscribes to <see cref="TranscriptionSetupStepViewModel.BrowseRequested"/> to open
/// a native file picker so the user can locate a pre-downloaded GGML Whisper model.
/// </summary>
public partial class TranscriptionSetupStepView : UserControl
{
    private TranscriptionSetupStepViewModel? _vm;

    /// <summary>Initialises a new <see cref="TranscriptionSetupStepView"/>.</summary>
    public TranscriptionSetupStepView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_vm is not null)
            _vm.BrowseRequested -= OnBrowseRequested;

        _vm = DataContext as TranscriptionSetupStepViewModel;

        if (_vm is not null)
            _vm.BrowseRequested += OnBrowseRequested;
    }

    private async void OnBrowseRequested()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || _vm is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title         = "Select Whisper model file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("GGML model") { Patterns = new[] { "*.bin" } },
                FilePickerFileTypes.All,
            },
        });

        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
                _vm.SetModelPath(path);
        }
    }
}
