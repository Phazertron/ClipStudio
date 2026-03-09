using ClipStudio.UI.ViewModels.WizardSteps;

namespace ClipStudio.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="FfmpegStepViewModel"/> covering initial state,
/// property derivations, and the Browse event.
/// </summary>
public sealed class FfmpegStepViewModelTests
{
    private readonly FfmpegStepViewModel _vm = new();

    // ---- Initial state ----

    [Fact]
    public void Constructor_DetectionStatus_IsChecking()
    {
        Assert.Equal(FfmpegDetectionStatus.Checking, _vm.DetectionStatus);
    }

    [Fact]
    public void Constructor_IsChecking_IsTrue()
    {
        Assert.True(_vm.IsChecking);
    }

    [Fact]
    public void Constructor_IsReady_IsFalse()
    {
        Assert.False(_vm.IsReady);
    }

    [Fact]
    public void Constructor_NeedsManualPath_IsFalse()
    {
        Assert.False(_vm.NeedsManualPath);
    }

    // ---- Status-driven property derivations ----

    [Fact]
    public void WhenStatusFoundBundled_IsReady_IsTrue()
    {
        _vm.DetectionStatus = FfmpegDetectionStatus.FoundBundled;

        Assert.True(_vm.IsReady);
        Assert.True(_vm.IsBundled);
        Assert.False(_vm.IsOnPath);
        Assert.False(_vm.NeedsManualPath);
        Assert.False(_vm.IsChecking);
    }

    [Fact]
    public void WhenStatusFoundOnPath_IsReady_IsTrue()
    {
        _vm.DetectionStatus = FfmpegDetectionStatus.FoundOnPath;

        Assert.True(_vm.IsReady);
        Assert.True(_vm.IsOnPath);
        Assert.False(_vm.IsBundled);
        Assert.False(_vm.NeedsManualPath);
        Assert.False(_vm.IsChecking);
    }

    [Fact]
    public void WhenStatusNotFound_NeedsManualPath_IsTrue()
    {
        _vm.DetectionStatus = FfmpegDetectionStatus.NotFound;

        Assert.True(_vm.NeedsManualPath);
        Assert.False(_vm.IsReady);
        Assert.False(_vm.IsChecking);
    }

    // ---- ResolvedFolder ----

    [Fact]
    public void ResolvedFolder_WhenReady_ReturnsDetectedFolder()
    {
        _vm.DetectionStatus = FfmpegDetectionStatus.FoundBundled;
        _vm.DetectedFolder  = @"C:\ffmpeg";
        _vm.ManualFolder    = @"C:\manual";

        Assert.Equal(@"C:\ffmpeg", _vm.ResolvedFolder);
    }

    [Fact]
    public void ResolvedFolder_WhenNotFound_ReturnsManualFolderTrimmed()
    {
        _vm.DetectionStatus = FfmpegDetectionStatus.NotFound;
        _vm.ManualFolder    = @"  C:\manual  ";

        Assert.Equal(@"C:\manual", _vm.ResolvedFolder);
    }

    [Fact]
    public void ResolvedFolder_WhenNotFound_EmptyManual_ReturnsEmptyString()
    {
        _vm.DetectionStatus = FfmpegDetectionStatus.NotFound;
        _vm.ManualFolder    = string.Empty;

        Assert.Equal(string.Empty, _vm.ResolvedFolder);
    }

    // ---- Browse event ----

    [Fact]
    public void BrowseCommand_RaisesBrowseRequestedEvent()
    {
        var raised = false;
        _vm.BrowseRequested += () => raised = true;

        _vm.BrowseCommand.Execute(null);

        Assert.True(raised);
    }
}
