using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.UI.ViewModels.WizardSteps;
using Moq;

namespace ClipStudio.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="FinishStepViewModel"/> covering theme radio-button mutual
/// exclusivity, settings persistence, and the Completed event.
/// </summary>
public sealed class FinishStepViewModelTests
{
    private readonly Mock<ISettingsService> _settingsMock;
    private readonly AppSettings            _settings;
    private readonly FinishStepViewModel    _vm;

    public FinishStepViewModelTests()
    {
        _settings     = new AppSettings();
        _settingsMock = new Mock<ISettingsService>();
        _settingsMock.Setup(s => s.Current).Returns(_settings);
        _settingsMock.Setup(s => s.SaveAsync(default)).Returns(Task.CompletedTask);

        _vm = new FinishStepViewModel(_settingsMock.Object);
    }

    // ---- Default state ----

    [Fact]
    public void Constructor_IsDarkTheme_IsTrue()
    {
        Assert.True(_vm.IsDarkTheme);
        Assert.False(_vm.IsLightTheme);
        Assert.False(_vm.IsSystemTheme);
    }

    // ---- Theme radio mutual exclusivity ----

    [Fact]
    public void SetIsLightTheme_True_ClearsDarkAndSystem()
    {
        _vm.IsLightTheme = true;

        Assert.True(_vm.IsLightTheme);
        Assert.False(_vm.IsDarkTheme);
        Assert.False(_vm.IsSystemTheme);
    }

    [Fact]
    public void SetIsSystemTheme_True_ClearsDarkAndLight()
    {
        _vm.IsSystemTheme = true;

        Assert.True(_vm.IsSystemTheme);
        Assert.False(_vm.IsDarkTheme);
        Assert.False(_vm.IsLightTheme);
    }

    [Fact]
    public void SetIsDarkTheme_True_ClearsLightAndSystem()
    {
        _vm.IsSystemTheme = true; // first switch away from dark
        _vm.IsDarkTheme   = true;

        Assert.True(_vm.IsDarkTheme);
        Assert.False(_vm.IsLightTheme);
        Assert.False(_vm.IsSystemTheme);
    }

    // ---- Settings persistence ----

    [Fact]
    public async Task FinishCommand_CallsSaveAsync()
    {
        await _vm.FinishCommand.ExecuteAsync(null);

        _settingsMock.Verify(s => s.SaveAsync(default), Times.Once);
    }

    [Fact]
    public async Task FinishCommand_SetsIsFirstRunFalse()
    {
        _settings.IsFirstRun = true;

        await _vm.FinishCommand.ExecuteAsync(null);

        Assert.False(_settings.IsFirstRun);
    }

    [Fact]
    public async Task FinishCommand_WithDarkSelected_SetsThemeToDark()
    {
        _vm.IsDarkTheme = true;

        await _vm.FinishCommand.ExecuteAsync(null);

        Assert.Equal("Dark", _settings.Theme);
    }

    [Fact]
    public async Task FinishCommand_WithLightSelected_SetsThemeToLight()
    {
        _vm.IsLightTheme = true;

        await _vm.FinishCommand.ExecuteAsync(null);

        Assert.Equal("Light", _settings.Theme);
    }

    [Fact]
    public async Task FinishCommand_WithSystemSelected_SetsThemeToSystem()
    {
        _vm.IsSystemTheme = true;

        await _vm.FinishCommand.ExecuteAsync(null);

        Assert.Equal("System", _settings.Theme);
    }

    [Fact]
    public async Task FinishCommand_WritesFfmpegFolder_FromResolvedFfmpegFolder()
    {
        _vm.ResolvedFfmpegFolder = @"C:\tools\ffmpeg";

        await _vm.FinishCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\tools\ffmpeg", _settings.FfmpegBinaryFolder);
    }

    // ---- IsFinishing state ----

    [Fact]
    public async Task FinishCommand_IsFinishing_FalseAfterCompletion()
    {
        await _vm.FinishCommand.ExecuteAsync(null);

        Assert.False(_vm.IsFinishing);
    }

    // ---- Completed event ----

    [Fact]
    public async Task FinishCommand_RaisesCompletedEvent()
    {
        var fired = false;
        _vm.Completed += () => fired = true;

        await _vm.FinishCommand.ExecuteAsync(null);

        Assert.True(fired);
    }
}
