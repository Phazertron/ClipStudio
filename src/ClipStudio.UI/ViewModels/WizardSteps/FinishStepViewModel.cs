using System;
using System.IO;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFMpegCore;

namespace ClipStudio.UI.ViewModels.WizardSteps;

/// <summary>
/// View model for the final wizard step.
/// Allows the user to choose a UI theme, then saves all gathered settings,
/// marks the first-run wizard as complete, and raises <see cref="Completed"/>.
/// </summary>
public sealed partial class FinishStepViewModel : WizardStepViewModel
{
    private readonly ISettingsService _settings;

    /// <inheritdoc/>
    public override string Title => "All Set!";

    /// <inheritdoc/>
    public override int StepNumber => 4;

    // ---- Theme selection ----

    /// <summary>Gets or sets whether the Dark theme is selected.</summary>
    [ObservableProperty]
    private bool _isDarkTheme = true;

    /// <summary>Gets or sets whether the Light theme is selected.</summary>
    [ObservableProperty]
    private bool _isLightTheme;

    /// <summary>Gets or sets whether the System theme is selected.</summary>
    [ObservableProperty]
    private bool _isSystemTheme;

    // ---- State ----

    /// <summary>Gets or sets whether the finish operation is currently running.</summary>
    [ObservableProperty]
    private bool _isFinishing;

    /// <summary>
    /// Gets or sets the resolved FFmpeg folder, set by <see cref="SetupWizardViewModel"/>
    /// just before this step becomes active.
    /// </summary>
    public string ResolvedFfmpegFolder { get; set; } = string.Empty;

    /// <summary>Raised when all settings have been saved and the main window should be shown.</summary>
    public event Action? Completed;

    /// <summary>Gets the command that saves settings and signals wizard completion.</summary>
    public IAsyncRelayCommand FinishCommand { get; }

    /// <summary>
    /// Initialises a new <see cref="FinishStepViewModel"/>.
    /// </summary>
    /// <param name="settings">Application settings service used to persist the wizard results.</param>
    public FinishStepViewModel(ISettingsService settings)
    {
        _settings     = settings;
        FinishCommand = new AsyncRelayCommand(FinishAsync);
    }

    // ---- Theme property callbacks ----

    partial void OnIsDarkThemeChanged(bool value)   { if (value) { IsLightTheme  = false; IsSystemTheme = false; } }
    partial void OnIsLightThemeChanged(bool value)  { if (value) { IsDarkTheme   = false; IsSystemTheme = false; } }
    partial void OnIsSystemThemeChanged(bool value) { if (value) { IsDarkTheme   = false; IsLightTheme  = false; } }

    // ---- Finish ----

    private async Task FinishAsync()
    {
        IsFinishing = true;
        try
        {
            var s = _settings.Current;
            s.Theme              = IsLightTheme ? "Light" : IsSystemTheme ? "System" : "Dark";
            s.FfmpegBinaryFolder = ResolvedFfmpegFolder;
            s.IsFirstRun         = false;
            await _settings.SaveAsync();
            ApplyFfmpegFolder(s.FfmpegBinaryFolder);
        }
        finally
        {
            IsFinishing = false;
        }

        Completed?.Invoke();
    }

    /// <summary>
    /// Applies the FFmpeg binary folder to FFMpegCore's global options,
    /// auto-detecting the bundled <c>ffmpeg/</c> folder when no explicit path is configured.
    /// </summary>
    private static void ApplyFfmpegFolder(string configuredFolder)
    {
        var folder = configuredFolder;

        if (string.IsNullOrWhiteSpace(folder))
        {
            var candidate = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
            if (Directory.Exists(candidate))
                folder = candidate;
        }

        if (!string.IsNullOrWhiteSpace(folder))
            GlobalFFOptions.Configure(options => options.BinaryFolder = folder);
    }
}
