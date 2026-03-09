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
    private readonly List<WizardStepViewModel> _steps;
    private readonly FfmpegStepViewModel _ffmpegStep;
    private readonly FinishStepViewModel _finishStep;

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
    public IReadOnlyList<StepIndicatorViewModel> StepIndicators { get; }

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
    /// FFmpeg detection runs synchronously during construction (all file-system checks, no I/O).
    /// When FFmpeg is already available — either bundled inside the installer or on the system PATH —
    /// the FFmpeg configuration step is omitted entirely from the wizard so the user is never asked
    /// about something that has already been resolved automatically.
    /// </summary>
    public SetupWizardViewModel(
        WelcomeStepViewModel welcome,
        SourceFoldersStepViewModel sourceFolders,
        FfmpegStepViewModel ffmpeg,
        FinishStepViewModel finish)
    {
        _ffmpegStep = ffmpeg;
        _finishStep = finish;

        // Run detection now (synchronous file checks) so we know whether to include the step.
        ffmpeg.DetectAsync().GetAwaiter().GetResult();

        if (ffmpeg.IsReady)
        {
            // FFmpeg found — wire its resolved folder directly to Finish and skip the UI step.
            finish.ResolvedFfmpegFolder = ffmpeg.ResolvedFolder;
            _steps = new List<WizardStepViewModel> { welcome, sourceFolders, finish };
        }
        else
        {
            // FFmpeg not found — include the configuration step so the user can locate it.
            _steps = new List<WizardStepViewModel> { welcome, sourceFolders, ffmpeg, finish };
        }

        _currentStep      = _steps[0];
        _currentStepIndex = 0;

        StepIndicators = _steps
            .Select((_, i) => new StepIndicatorViewModel(i + 1))
            .ToList();

        StepIndicators[0].IsCurrent = true;

        // Forward the finish step's Completed event
        _finishStep.Completed += () => Completed?.Invoke();

        NextCommand = new AsyncRelayCommand(GoNextAsync);
        BackCommand = new RelayCommand(GoBack);
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
