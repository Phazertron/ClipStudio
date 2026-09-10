using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;
using ClipStudio.Tests.Fakes;
using ClipStudio.UI.Services;
using ClipStudio.UI.ViewModels;
using ClipStudio.UI.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ClipStudio.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="SettingsViewModel"/> as a shell: it owns the section list, one load
/// that fans out to every section, and one save that collects from every section.
/// </summary>
public sealed class SettingsViewModelTests
{
    private readonly Mock<ISettingsService>        _settingsMock = new();
    private readonly Mock<ISourceFolderRepository> _foldersMock  = new();
    private readonly Mock<ILibraryWatcherService>  _watcherMock  = new();
    private readonly Mock<IImportService>          _importMock   = new();
    private readonly Mock<ISoundService>           _soundMock    = new();
    private readonly Mock<IClipRepository>         _clipsMock    = new();
    private readonly Mock<IHighlightRepository>    _highlightsMock = new();
    private readonly Mock<ILibraryHealthCheckService> _healthMock = new();
    private readonly Mock<IDuplicateClipFinder> _duplicatesMock = new();
    private readonly FakeBackgroundTaskService _tasks = new();
    private readonly AppSettings                   _settings     = new();
    private readonly SettingsViewModel             _vm;

    public SettingsViewModelTests()
    {
        _settingsMock.Setup(x => x.Current).Returns(_settings);
        _foldersMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<SourceFolder>());
        _clipsMock.Setup(x => x.CountWithoutFileHashAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(0);
        _duplicatesMock.Setup(x => x.LastGroups).Returns(new List<DuplicateClipGroup>());

        var services = new ServiceCollection();
        services.AddScoped(_ => _clipsMock.Object);
        services.AddScoped(_ => _highlightsMock.Object);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        _vm = new SettingsViewModel(
            _settingsMock.Object,
            _foldersMock.Object,
            _watcherMock.Object,
            _importMock.Object,
            scopeFactory,
            _soundMock.Object,
            _healthMock.Object,
            _duplicatesMock.Object,
            _tasks);
    }

    [Fact]
    public void ExposesEverySectionAndSelectsTheFirstOne()
    {
        Assert.Equal(8, _vm.Sections.Count);
        Assert.Same(_vm.SourceFoldersSection, _vm.Sections[0]);
        Assert.Same(_vm.SourceFoldersSection, _vm.SelectedSection);
        Assert.All(_vm.Sections, s => Assert.IsAssignableFrom<SettingsSectionViewModel>(s));
    }

    [Fact]
    public async Task LoadFansOutToEverySection()
    {
        _settings.ThumbnailOffsetSeconds = 9;
        _settings.MinimumLogLevel        = "Debug";
        _settings.TranscriptionEnabled   = true;

        await _vm.LoadAsync();

        Assert.Equal(9, _vm.PreferencesSection.ThumbnailOffsetSeconds);
        Assert.Equal("Debug", _vm.MaintenanceSection.MinimumLogLevel);
        Assert.True(_vm.TranscriptionSection.TranscriptionEnabled);
        Assert.False(_vm.IsLoading);
    }

    [Fact]
    public async Task SaveCollectsFromEverySection()
    {
        // One Save button covers the whole page, so a change made in any section must reach disk.
        _vm.PreferencesSection.ThumbnailOffsetSeconds  = 4;
        _vm.MaintenanceSection.MinimumLogLevel         = "Warning";
        _vm.TranscriptionSection.TranscriptionEnabled  = true;

        await _vm.SavePreferencesCommand.ExecuteAsync(null);

        Assert.Equal(4, _settings.ThumbnailOffsetSeconds);
        Assert.Equal("Warning", _settings.MinimumLogLevel);
        Assert.True(_settings.TranscriptionEnabled);
        _settingsMock.Verify(x => x.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Settings saved.", _vm.StatusMessage);
    }

    [Fact]
    public async Task LoadLeavesAnInProgressRepairAlone()
    {
        // Navigating away and back re-enters the load. Clearing the flag or the message there hid
        // the progress bar while the repair was still running.
        ISettingsSectionHost host = _vm;
        host.SetRepairing(true);
        host.StatusMessage = "Repairing library...";

        await _vm.LoadAsync();

        Assert.True(_vm.IsLoading);
        Assert.Equal("Repairing library...", _vm.StatusMessage);

        host.SetRepairing(false);
        await _vm.LoadAsync();

        Assert.False(_vm.IsLoading);
        Assert.Null(_vm.StatusMessage);
    }

    [Fact]
    public void ForwardsAnUnreviewedCountRefreshToTheMainWindow()
    {
        var calls = 0;
        _vm.UnreviewedCountRefreshRequested = () => calls++;

        ((ISettingsSectionHost)_vm).RequestUnreviewedCountRefresh();

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ARepairRefreshesTheUnhashedClipCount()
    {
        // The repair is what clears the warning, so the count has to be re-read when it finishes.
        _clipsMock.Setup(x => x.CountWithoutFileHashAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(3);

        await ((ISettingsSectionHost)_vm).NotifyLibraryRepairedAsync();

        Assert.Equal(3, _vm.PreferencesSection.UnhashedClipCount);
    }
}
