using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.UI.ViewModels;
using ClipStudio.UI.ViewModels.WizardSteps;
using Moq;

namespace ClipStudio.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="SetupWizardViewModel"/> covering navigation, step indicators,
/// labels, and the Completed event.
/// </summary>
public sealed class SetupWizardViewModelTests
{
    private readonly Mock<ISettingsService> _settingsMock;
    private readonly AppSettings _settings;
    private readonly SetupWizardViewModel _wizard;

    public SetupWizardViewModelTests()
    {
        _settings = new AppSettings();
        _settingsMock = new Mock<ISettingsService>();
        _settingsMock.Setup(s => s.Current).Returns(_settings);
        _settingsMock.Setup(s => s.SaveAsync(default)).Returns(Task.CompletedTask);

        var welcome        = new WelcomeStepViewModel();
        var sourceFolders  = new SourceFoldersStepViewModel(
            new Mock<ClipStudio.Core.Interfaces.ISourceFolderRepository>().Object,
            new Mock<ILibraryWatcherService>().Object);
        var ffmpeg         = new FfmpegStepViewModel();
        var transcription  = new TranscriptionSetupStepViewModel(_settingsMock.Object);
        var finish         = new FinishStepViewModel(_settingsMock.Object);

        _wizard = new SetupWizardViewModel(welcome, sourceFolders, ffmpeg, transcription, finish);
    }

    // ---- Initial state ----

    [Fact]
    public void Constructor_InitialisesAtStepIndex0()
    {
        Assert.Equal(0, _wizard.CurrentStepIndex);
    }

    [Fact]
    public void Constructor_CurrentStep_IsWelcomeStep()
    {
        Assert.IsType<WelcomeStepViewModel>(_wizard.CurrentStep);
    }

    [Fact]
    public void Constructor_CanGoBack_IsFalse()
    {
        Assert.False(_wizard.CanGoBack);
    }

    [Fact]
    public void Constructor_IsLastStep_IsFalse()
    {
        Assert.False(_wizard.IsLastStep);
    }

    [Fact]
    public void Constructor_NextButtonLabel_IsNext()
    {
        Assert.Equal("Next", _wizard.NextButtonLabel);
    }

    [Fact]
    public void Constructor_StepLabel_IsStep1OfTotal()
    {
        Assert.Equal($"Step 1 of {_wizard.TotalSteps}", _wizard.StepLabel);
    }

    [Fact]
    public void Constructor_ProgressPercent_IsFirstStepFraction()
    {
        var expected = 1.0 / _wizard.TotalSteps * 100.0;
        Assert.Equal(expected, _wizard.ProgressPercent, precision: 3);
    }

    [Fact]
    public void Constructor_StepIndicators_CountMatchesTotalSteps()
    {
        Assert.Equal(_wizard.TotalSteps, _wizard.StepIndicators.Count);
    }

    [Fact]
    public void Constructor_FirstIndicator_IsCurrentNotCompleted()
    {
        Assert.True(_wizard.StepIndicators[0].IsCurrent);
        Assert.False(_wizard.StepIndicators[0].IsCompleted);
    }

    [Fact]
    public void Constructor_OtherIndicators_AreNeitherCurrentNorCompleted()
    {
        for (var i = 1; i < _wizard.StepIndicators.Count; i++)
        {
            Assert.False(_wizard.StepIndicators[i].IsCurrent);
            Assert.False(_wizard.StepIndicators[i].IsCompleted);
        }
    }

    // ---- Forward navigation ----

    [Fact]
    public async Task NextCommand_AdvancesStepIndex()
    {
        await _wizard.NextCommand.ExecuteAsync(null);

        Assert.Equal(1, _wizard.CurrentStepIndex);
    }

    [Fact]
    public async Task NextCommand_CurrentStep_ChangesToSourceFoldersStep()
    {
        await _wizard.NextCommand.ExecuteAsync(null);

        Assert.IsType<SourceFoldersStepViewModel>(_wizard.CurrentStep);
    }

    [Fact]
    public async Task NextCommand_CanGoBack_BecomesTrue()
    {
        await _wizard.NextCommand.ExecuteAsync(null);

        Assert.True(_wizard.CanGoBack);
    }

    [Fact]
    public async Task NextCommand_StepIndicators_FirstIsCompletedSecondIsCurrent()
    {
        await _wizard.NextCommand.ExecuteAsync(null);

        Assert.True(_wizard.StepIndicators[0].IsCompleted);
        Assert.False(_wizard.StepIndicators[0].IsCurrent);
        Assert.True(_wizard.StepIndicators[1].IsCurrent);
        Assert.False(_wizard.StepIndicators[1].IsCompleted);
    }

    // ---- Back navigation ----

    [Fact]
    public async Task BackCommand_FromStep1_ReturnToStep0()
    {
        await _wizard.NextCommand.ExecuteAsync(null);
        _wizard.BackCommand.Execute(null);

        Assert.Equal(0, _wizard.CurrentStepIndex);
        Assert.IsType<WelcomeStepViewModel>(_wizard.CurrentStep);
    }

    [Fact]
    public async Task BackCommand_FromStep1_CanGoBack_BecomesFalse()
    {
        await _wizard.NextCommand.ExecuteAsync(null);
        _wizard.BackCommand.Execute(null);

        Assert.False(_wizard.CanGoBack);
    }

    // ---- Last step state ----

    [Fact]
    public async Task WhenAtLastStep_IsLastStep_IsTrue()
    {
        for (var i = 0; i < _wizard.TotalSteps - 1; i++)
            await _wizard.NextCommand.ExecuteAsync(null);

        Assert.True(_wizard.IsLastStep);
    }

    [Fact]
    public async Task WhenAtLastStep_NextButtonLabel_IsGetStarted()
    {
        for (var i = 0; i < _wizard.TotalSteps - 1; i++)
            await _wizard.NextCommand.ExecuteAsync(null);

        Assert.Equal("Get Started", _wizard.NextButtonLabel);
    }

    // ---- Completed event ----

    [Fact]
    public async Task NextCommand_OnLastStep_RaisesCompletedEvent()
    {
        // Advance to the last step (Finish)
        for (var i = 0; i < _wizard.TotalSteps - 1; i++)
            await _wizard.NextCommand.ExecuteAsync(null);

        var fired = false;
        _wizard.Completed += () => fired = true;

        // Execute Next on the last step -> calls FinishCommand
        await _wizard.NextCommand.ExecuteAsync(null);

        Assert.True(fired);
    }
}
