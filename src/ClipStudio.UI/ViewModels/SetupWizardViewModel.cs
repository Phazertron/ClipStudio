using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClipStudio.UI.ViewModels.WizardSteps;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Top-level view model for the first-run setup wizard.
/// Manages the ordered list of wizard steps, drives Next/Back navigation,
/// and surfaces the <see cref="Completed"/> event to <c>App.axaml.cs</c>
/// when the user finishes the wizard.
/// </summary>
public sealed partial class SetupWizardViewModel : ViewModelBase
{
    private readonly WelcomeStepViewModel _welcomeStep;
    private readonly SourceFoldersStepViewModel _sourceFoldersStep;
    private readonly FfmpegStepViewModel _ffmpegStep;
    private readonly TranscriptionSetupStepViewModel _transcriptionStep;
    private readonly FinishStepViewModel _finishStep;

    private List<WizardStepViewModel> _steps;
    private bool _isInitialised;

    // ---- Navigation state ----

    /// <summary>Gets or sets the wizard step that is currently visible.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(NextButtonLabel))]
    [NotifyPropertyChangedFor(nameof(StepLabel))]
    private WizardStepViewModel _currentStep;

    /// <summary>Gets or sets the 0-based index of the currently visible step.</summary>
    [ObservableProperty]
    private int _currentStepIndex;

    /// <summary>Gets the total number of steps in the wizard.</summary>
    public int TotalSteps => _steps.Count;

    /// <summary>Gets the step indicator view models for the navigation dots shown in the header.</summary>
    [ObservableProperty]
    private IReadOnlyList<StepIndicatorViewModel> _stepIndicators;

    /// <summary>Gets a value indicating whether the Back button should be enabled.</summary>
    public bool CanGoBack => CurrentStepIndex > 0;

    /// <summary>Gets a value indicating whether the current step is the last one.</summary>
    public bool IsLastStep => CurrentStepIndex == _steps.Count - 1;

    /// <summary>Gets the label for the primary forward button ("Next" or "Get Started" on the last step).</summary>
    public string NextButtonLabel => IsLastStep ? "Get Started" : "Next";

    /// <summary>Gets the "Step X of Y" label shown in the header.</summary>
    public string StepLabel => $"Step {CurrentStepIndex + 1} of {TotalSteps}";

    /// <summary>Gets the current wizard progress as a percentage (0-100).</summary>
    public double ProgressPercent => (CurrentStepIndex + 1.0) / TotalSteps * 100.0;

    // ---- Commands ----

    /// <summary>Gets the command that advances to the next step, or finishes the wizard on the last step.</summary>
    public IAsyncRelayCommand NextCommand { get; }

    /// <summary>Gets the command that returns to the previous step.</summary>
    public IRelayCommand BackCommand { get; }

    /// <summary>Raised when all settings have been saved and the main window should be shown.</summary>
    public event Action? Completed;

    /// <summary>
    /// Initialises a new <see cref="SetupWizardViewModel"/> and wires up the step sequence.
    /// The constructor performs no I/O: the wizard starts with the full step sequence, including
    /// the FFmpeg configuration step. Call <see cref="InitializeAsync"/> once the view model is
    /// bound to run FFmpeg detection and drop that step when FFmpeg has already been resolved
    /// automatically, so the user is never asked about something that is already handled.
    /// </summary>
    public SetupWizardViewModel(
        WelcomeStepViewModel welcome,
        SourceFoldersStepViewModel sourceFolders,
        FfmpegStepViewModel ffmpeg,
        TranscriptionSetupStepViewModel transcription,
        FinishStepViewModel finish)
    {
        _welcomeStep       = welcome;
        _sourceFoldersStep = sourceFolders;
        _ffmpegStep        = ffmpeg;
        _transcriptionStep = transcription;
        _finishStep        = finish;

        // Start with the complete sequence; InitializeAsync trims it once detection has run.
        _steps            = BuildSteps(includeFfmpegStep: true);
        _currentStep      = _steps[0];
        _currentStepIndex = 0;
        _stepIndicators   = BuildIndicators();

        // Forward the finish step's Completed event
        _finishStep.Completed += () => Completed?.Invoke();

        NextCommand = new AsyncRelayCommand(GoNextAsync);
        BackCommand = new RelayCommand(GoBack);
    }

    /// <summary>
    /// Runs FFmpeg detection asynchronously and removes the FFmpeg configuration step when a
    /// usable FFmpeg installation was found. Safe to call more than once; only the first call
    /// has an effect. Must be called while the wizard is still on the first step.
    /// </summary>
    /// <returns>A task that completes once detection has run and the step list has settled.</returns>
    public async Task InitializeAsync()
    {
        if (_isInitialised)
            return;

        _isInitialised = true;

        await _ffmpegStep.DetectAsync();

        if (!_ffmpegStep.IsReady)
            return;

        // FFmpeg found - wire its resolved folder directly to Finish and skip the UI step.
        _finishStep.ResolvedFfmpegFolder = _ffmpegStep.ResolvedFolder;

        _steps = BuildSteps(includeFfmpegStep: false);

        CurrentStepIndex = Math.Min(CurrentStepIndex, _steps.Count - 1);
        CurrentStep      = _steps[CurrentStepIndex];
        StepIndicators   = BuildIndicators();

        OnPropertyChanged(nameof(TotalSteps));
        OnPropertyChanged(nameof(StepLabel));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(NextButtonLabel));
        OnPropertyChanged(nameof(ProgressPercent));

        UpdateIndicators();
    }

    /// <summary>
    /// Builds the ordered step sequence, optionally including the FFmpeg configuration step.
    /// </summary>
    /// <param name="includeFfmpegStep">
    /// <see langword="true"/> to include the FFmpeg step; <see langword="false"/> when FFmpeg
    /// has already been resolved and the step would be redundant.
    /// </param>
    /// <returns>The ordered list of steps the wizard should walk through.</returns>
    private List<WizardStepViewModel> BuildSteps(bool includeFfmpegStep) =>
        includeFfmpegStep
            ? new List<WizardStepViewModel> { _welcomeStep, _sourceFoldersStep, _ffmpegStep, _transcriptionStep, _finishStep }
            : new List<WizardStepViewModel> { _welcomeStep, _sourceFoldersStep, _transcriptionStep, _finishStep };

    /// <summary>
    /// Builds a fresh set of header indicators matching the current step sequence, with the
    /// indicator for <see cref="CurrentStepIndex"/> marked as current.
    /// </summary>
    /// <returns>One indicator view model per step.</returns>
    private IReadOnlyList<StepIndicatorViewModel> BuildIndicators()
    {
        var indicators = _steps
            .Select((_, i) => new StepIndicatorViewModel(i + 1))
            .ToList();

        for (var i = 0; i < indicators.Count; i++)
        {
            indicators[i].IsCurrent   = i == CurrentStepIndex;
            indicators[i].IsCompleted = i < CurrentStepIndex;
        }

        return indicators;
    }

    // ---- Navigation ----

    private async Task GoNextAsync()
    {
        if (IsLastStep)
        {
            // Delegate to the finish step's own finish command
            await _finishStep.FinishCommand.ExecuteAsync(null);
            return;
        }

        var nextIndex = CurrentStepIndex + 1;

        // Before entering the Finish step, pass the FFmpeg folder resolved by the FFmpeg step.
        // (When the FFmpeg step is included and the user supplied a manual path, this picks it up.)
        if (_steps[nextIndex] is FinishStepViewModel finishStep)
            finishStep.ResolvedFfmpegFolder = _ffmpegStep.ResolvedFolder;

        // Apply transcription settings when leaving the transcription step.
        if (CurrentStep is TranscriptionSetupStepViewModel)
            _ = _transcriptionStep.ApplyAsync();

        CurrentStepIndex = nextIndex;
        CurrentStep      = _steps[nextIndex];

        UpdateIndicators();
    }

    private void GoBack()
    {
        if (CurrentStepIndex <= 0)
            return;

        CurrentStepIndex--;
        CurrentStep = _steps[CurrentStepIndex];

        UpdateIndicators();
    }

    private void UpdateIndicators()
    {
        for (var i = 0; i < StepIndicators.Count; i++)
        {
            StepIndicators[i].IsCurrent   = i == CurrentStepIndex;
            StepIndicators[i].IsCompleted = i < CurrentStepIndex;
        }
    }
}
