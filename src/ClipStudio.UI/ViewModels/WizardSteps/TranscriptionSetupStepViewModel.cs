using System.IO;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels.WizardSteps;

/// <summary>
/// Wizard step that introduces local voice transcription and lets the user opt in and
/// browse for a pre-downloaded GGML Whisper model file.
/// This step is intentionally low-friction: enabling transcription is optional and
/// the model can also be configured later in Settings > Transcription.
/// </summary>
public sealed partial class TranscriptionSetupStepViewModel : WizardStepViewModel
{
    private readonly ISettingsService _settings;

    /// <inheritdoc/>
    public override string Title => "Voice Transcription (Optional)";

    /// <inheritdoc/>
    public override int StepNumber => 3;

    /// <summary>Gets or sets whether the user has opted in to local voice transcription.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowModelPicker))]
    private bool _isEnabled;

    /// <summary>Gets or sets the absolute path to the selected GGML Whisper model file.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelFileName))]
    private string _modelPath = string.Empty;

    /// <summary>Gets the filename of the selected model, or a placeholder when none is chosen.</summary>
    public string ModelFileName =>
        string.IsNullOrWhiteSpace(ModelPath) ? "(no model selected)" : Path.GetFileName(ModelPath);

    /// <summary>Gets a value indicating whether the model picker row should be visible.</summary>
    public bool ShowModelPicker => IsEnabled;

    /// <summary>Gets the command that opens a file picker to browse for a model file.</summary>
    public IAsyncRelayCommand BrowseModelCommand { get; }

    /// <summary>Raised when the user wants to open the file picker from the view code-behind.</summary>
    public event System.Action? BrowseRequested;

    /// <summary>
    /// Initialises a new <see cref="TranscriptionSetupStepViewModel"/>.
    /// </summary>
    public TranscriptionSetupStepViewModel(ISettingsService settings)
    {
        _settings          = settings;
        BrowseModelCommand = new AsyncRelayCommand(BrowseAsync);
    }

    /// <summary>
    /// Applies the selected settings to the current <see cref="AppSettings"/> snapshot.
    /// Called by <see cref="SetupWizardViewModel"/> just before moving to the next step.
    /// </summary>
    public async Task ApplyAsync()
    {
        var s = _settings.Current;
        s.TranscriptionEnabled   = IsEnabled;
        s.TranscriptionModelPath = ModelPath.Trim();
        await _settings.SaveAsync();
    }

    /// <summary>Sets the model path after the view code-behind resolves it from the file picker.</summary>
    public void SetModelPath(string path) => ModelPath = path;

    private Task BrowseAsync()
    {
        BrowseRequested?.Invoke();
        return Task.CompletedTask;
    }
}
