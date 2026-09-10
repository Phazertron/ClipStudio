using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.UI.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace ClipStudio.UI.ViewModels.Settings;

/// <summary>
/// The Transcription section of the Settings page: the local Whisper model, inference backend,
/// language, SRT output folder and the auto-on-import policy.
/// </summary>
public sealed partial class TranscriptionSectionViewModel : SettingsSectionViewModel
{
    private readonly ISettingsService _settings;

    /// <inheritdoc/>
    public override string Title => "Transcription";

    /// <inheritdoc/>
    public override MaterialIconKind Icon => MaterialIconKind.ClosedCaptionOutline;

    /// <summary>Gets or sets whether local voice transcription is enabled.</summary>
    [ObservableProperty]
    private bool _transcriptionEnabled;

    /// <summary>Gets or sets the absolute path to the GGML Whisper model file.</summary>
    [ObservableProperty]
    private string _transcriptionModelPath = string.Empty;

    /// <summary>Gets or sets the transcription inference backend display string.</summary>
    [ObservableProperty]
    private string _transcriptionBackend = "Auto (recommended)";

    /// <summary>Gets or sets the language option selected for transcription.</summary>
    [ObservableProperty]
    private LanguageOption _selectedTranscriptionLanguage = TranscriptionLanguageOptions[0];

    /// <summary>Gets or sets the folder where generated SRT files are saved (empty = next to clip).</summary>
    [ObservableProperty]
    private string _transcriptionSrtFolder = string.Empty;

    /// <summary>Gets or sets whether the experimental speaker diarization pass is enabled (no-op in v1).</summary>
    [ObservableProperty]
    private bool _transcriptionEnableDiarization;

    /// <summary>
    /// Gets or sets whether imported clips are automatically transcribed on import using the
    /// track indices configured in <see cref="TranscriptionAutoOnImportTrackIndices"/>.
    /// </summary>
    [ObservableProperty]
    private bool _transcriptionAutoOnImport;

    /// <summary>
    /// Gets or sets the comma-separated FFmpeg audio stream indices to mix when auto-transcribing
    /// on import (for example <c>"0"</c> or <c>"0,2"</c>).
    /// </summary>
    [ObservableProperty]
    private string _transcriptionAutoOnImportTrackIndices = "0";

    /// <summary>Gets the list of backend display strings for the ComboBox.</summary>
    public static IReadOnlyList<string> TranscriptionBackendOptions { get; } =
        new[] { "Auto (recommended)", "CPU only", "Vulkan (GPU)" };

    /// <summary>Gets the list of language options for the transcription language picker.</summary>
    public static IReadOnlyList<LanguageOption> TranscriptionLanguageOptions { get; } =
        TranscriptionSetupDialogViewModel.BuildLanguageOptionsList();

    /// <summary>
    /// Exposes the underlying settings service so that the section view's code-behind can pass it
    /// to the model management dialog, which owns its own persistence.
    /// </summary>
    public ISettingsService SettingsService => _settings;

    /// <summary>Initialises a new <see cref="TranscriptionSectionViewModel"/>.</summary>
    /// <param name="host">The settings page hosting this section.</param>
    /// <param name="settings">The application settings service.</param>
    public TranscriptionSectionViewModel(ISettingsSectionHost host, ISettingsService settings)
        : base(host)
    {
        _settings = settings;
    }

    /// <summary>
    /// Reloads the whole settings page, so a model installed through the management dialog is
    /// picked up here.
    /// </summary>
    /// <remarks>
    /// The dialog writes settings itself rather than through this section, so the section's own
    /// fields are stale until the page re-reads the snapshot.
    /// </remarks>
    public Task ReloadPageAsync() => Host.ReloadAsync();

    /// <inheritdoc/>
    public override void LoadFrom(AppSettings settings)
    {
        TranscriptionEnabled                  = settings.TranscriptionEnabled;
        TranscriptionModelPath                = settings.TranscriptionModelPath;
        TranscriptionSrtFolder                = settings.TranscriptionSrtFolder;
        TranscriptionEnableDiarization        = settings.TranscriptionEnableDiarization;
        TranscriptionAutoOnImport             = settings.TranscriptionAutoOnImport;
        TranscriptionAutoOnImportTrackIndices = settings.TranscriptionAutoOnImportTrackIndices;

        SelectedTranscriptionLanguage = TranscriptionLanguageOptions
            .FirstOrDefault(o => o.Code == settings.TranscriptionLanguage)
            ?? TranscriptionLanguageOptions[0];

        TranscriptionBackend = settings.TranscriptionBackend switch
        {
            ClipStudio.Core.Enums.TranscriptionBackend.Cpu    => "CPU only",
            ClipStudio.Core.Enums.TranscriptionBackend.Vulkan => "Vulkan (GPU)",
            _                                                 => "Auto (recommended)"
        };
    }

    /// <inheritdoc/>
    public override void ApplyTo(AppSettings settings)
    {
        settings.TranscriptionEnabled                  = TranscriptionEnabled;
        settings.TranscriptionModelPath                = TranscriptionModelPath.Trim();
        settings.TranscriptionLanguage                 = SelectedTranscriptionLanguage.Code;
        settings.TranscriptionSrtFolder                = TranscriptionSrtFolder.Trim();
        settings.TranscriptionEnableDiarization        = TranscriptionEnableDiarization;
        settings.TranscriptionAutoOnImport             = TranscriptionAutoOnImport;
        settings.TranscriptionAutoOnImportTrackIndices = TranscriptionAutoOnImportTrackIndices.Trim();

        settings.TranscriptionBackend = TranscriptionBackend switch
        {
            "CPU only"     => ClipStudio.Core.Enums.TranscriptionBackend.Cpu,
            "Vulkan (GPU)" => ClipStudio.Core.Enums.TranscriptionBackend.Vulkan,
            _              => ClipStudio.Core.Enums.TranscriptionBackend.Auto
        };
    }
}
