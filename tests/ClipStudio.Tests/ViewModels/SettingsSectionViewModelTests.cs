using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;
using ClipStudio.Tests.Fakes;
using ClipStudio.UI.ViewModels;
using ClipStudio.UI.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ClipStudio.Tests.ViewModels;

/// <summary>
/// Unit tests for the Settings page sections, driven through
/// <see cref="FakeSettingsSectionHost"/> rather than the page.
/// </summary>
/// <remarks>
/// Every section is a mapping between <see cref="AppSettings"/> and observable fields, so the
/// property worth asserting is the round trip: what is loaded is what is written back. The two
/// sections that do more than map - the unhashed-clip warning and the repair pass - get their own
/// tests below.
/// </remarks>
public sealed class SettingsSectionViewModelTests
{
    private readonly FakeSettingsSectionHost _host = new();
    private readonly FakeBackgroundTaskService _tasks = new();

    /// <summary>Builds a scope factory whose scopes resolve the given services.</summary>
    private static IServiceScopeFactory BuildScopeFactory(Action<ServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        configure?.Invoke(services);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    // ---- Preferences ----

    [Fact]
    public void Preferences_RoundTripsEverySettingItOwns()
    {
        var clips = new Mock<IClipRepository>();
        clips.Setup(x => x.CountWithoutFileHashAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var vm = new PreferencesSectionViewModel(
            _host, BuildScopeFactory(s => s.AddScoped(_ => clips.Object)));

        var source = new AppSettings
        {
            ThumbnailOffsetSeconds       = 12,
            ScreenshotOutputFolder       = @"C:\shots",
            FfmpegBinaryFolder           = @"C:\ffmpeg",
            AutoMarkReviewedOnTagAdd     = false,
            AutoPlayOnOpen               = false,
            AutoScanAtStartup            = true,
            ContentHashingEnabled        = false,
            CacheAudioPreviews           = false,
            TrashExpiredSendToRecycleBin = false,
            AutoApplyMePlayerOnImport    = false,
            ShowImagesInLists            = false,
            SoundEffectsEnabled          = false,
        };

        vm.LoadFrom(source);

        var target = new AppSettings();
        vm.ApplyTo(target);

        Assert.Equal(12, target.ThumbnailOffsetSeconds);
        Assert.Equal(@"C:\shots", target.ScreenshotOutputFolder);
        Assert.Equal(@"C:\ffmpeg", target.FfmpegBinaryFolder);
        Assert.False(target.AutoMarkReviewedOnTagAdd);
        Assert.False(target.AutoPlayOnOpen);
        Assert.True(target.AutoScanAtStartup);
        Assert.False(target.ContentHashingEnabled);
        Assert.False(target.CacheAudioPreviews);
        Assert.False(target.TrashExpiredSendToRecycleBin);
        Assert.False(target.AutoApplyMePlayerOnImport);
        Assert.False(target.ShowImagesInLists);
        Assert.False(target.SoundEffectsEnabled);
    }

    [Fact]
    public async Task Preferences_CountsUnhashedClipsOnRefresh()
    {
        var clips = new Mock<IClipRepository>();
        clips.Setup(x => x.CountWithoutFileHashAsync(It.IsAny<CancellationToken>())).ReturnsAsync(7);

        var vm = new PreferencesSectionViewModel(
            _host, BuildScopeFactory(s => s.AddScoped(_ => clips.Object)));
        vm.ContentHashingEnabled = true;

        await vm.RefreshAsync();

        Assert.Equal(7, vm.UnhashedClipCount);
        Assert.True(vm.ShowUnhashedClipNote);
        Assert.Contains("7 clips", vm.UnhashedClipNote);
    }

    [Fact]
    public async Task Preferences_HidesUnhashedNoteWhenHashingIsOff()
    {
        // With hashing off nothing is being matched anyway, so the warning would be noise.
        var clips = new Mock<IClipRepository>();
        clips.Setup(x => x.CountWithoutFileHashAsync(It.IsAny<CancellationToken>())).ReturnsAsync(7);

        var vm = new PreferencesSectionViewModel(
            _host, BuildScopeFactory(s => s.AddScoped(_ => clips.Object)));
        vm.ContentHashingEnabled = false;

        await vm.RefreshAsync();

        Assert.Equal(7, vm.UnhashedClipCount);
        Assert.False(vm.ShowUnhashedClipNote);
    }

    [Fact]
    public async Task Preferences_FallsBackToZeroWhenTheCountCannotBeRead()
    {
        // The count is advisory; a repository failure must not break the settings page.
        var clips = new Mock<IClipRepository>();
        clips.Setup(x => x.CountWithoutFileHashAsync(It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("no database"));

        var vm = new PreferencesSectionViewModel(
            _host, BuildScopeFactory(s => s.AddScoped(_ => clips.Object)));
        vm.ContentHashingEnabled = true;

        await vm.RefreshAsync();

        Assert.Equal(0, vm.UnhashedClipCount);
        Assert.False(vm.ShowUnhashedClipNote);
    }

    // ---- Transcription ----

    [Theory]
    [InlineData(TranscriptionBackend.Auto,   "Auto (recommended)")]
    [InlineData(TranscriptionBackend.Cpu,    "CPU only")]
    [InlineData(TranscriptionBackend.Vulkan, "Vulkan (GPU)")]
    public void Transcription_RoundTripsTheBackendThroughItsDisplayString(
        TranscriptionBackend backend, string display)
    {
        var vm = BuildTranscriptionSection();

        vm.LoadFrom(new AppSettings { TranscriptionBackend = backend });
        Assert.Equal(display, vm.TranscriptionBackend);

        var target = new AppSettings();
        vm.ApplyTo(target);
        Assert.Equal(backend, target.TranscriptionBackend);
    }

    [Fact]
    public void Transcription_FallsBackToTheFirstLanguageWhenTheCodeIsUnknown()
    {
        var vm = BuildTranscriptionSection();

        vm.LoadFrom(new AppSettings { TranscriptionLanguage = "not-a-language" });

        Assert.Equal(TranscriptionSectionViewModel.TranscriptionLanguageOptions[0],
                     vm.SelectedTranscriptionLanguage);
    }

    [Fact]
    public void Transcription_TrimsThePathsItWritesBack()
    {
        var vm = BuildTranscriptionSection();
        vm.TranscriptionModelPath                = "  model.bin  ";
        vm.TranscriptionSrtFolder                = @"  C:\srt  ";
        vm.TranscriptionAutoOnImportTrackIndices = " 0,2 ";

        var target = new AppSettings();
        vm.ApplyTo(target);

        Assert.Equal("model.bin", target.TranscriptionModelPath);
        Assert.Equal(@"C:\srt", target.TranscriptionSrtFolder);
        Assert.Equal("0,2", target.TranscriptionAutoOnImportTrackIndices);
    }

    private TranscriptionSectionViewModel BuildTranscriptionSection()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(x => x.Current).Returns(new AppSettings());
        return new TranscriptionSectionViewModel(_host, settings.Object);
    }

    // ---- Maintenance ----

    [Fact]
    public void Maintenance_RoundTripsTheLogLevel()
    {
        var vm = new MaintenanceSectionViewModel(_host, BuildScopeFactory(), _tasks);

        vm.LoadFrom(new AppSettings { MinimumLogLevel = "Information" });
        Assert.Equal("Information", vm.MinimumLogLevel);

        var target = new AppSettings();
        vm.ApplyTo(target);
        Assert.Equal("Information", target.MinimumLogLevel);
    }

    [Fact]
    public async Task Maintenance_RunsTheSanitizerAndClearsTheRepairFlagAfterwards()
    {
        var sanitizer = new Mock<ILibrarySanitizerService>();
        var vm = new MaintenanceSectionViewModel(
            _host, BuildScopeFactory(s => s.AddScoped(_ => sanitizer.Object)), _tasks);

        await vm.RepairLibraryCommand.ExecuteAsync(null);

        sanitizer.Verify(
            x => x.SanitizeAsync(It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(new[] { true, false }, _host.RepairingChanges);
        Assert.False(vm.IsRepairRunning);
        Assert.Equal(1, _host.LibraryRepairedCalls);
    }

    [Fact]
    public async Task Maintenance_ReportsAFailedRepairRatherThanThrowingOutOfTheCommand()
    {
        // A failed repair must not leave the page stuck showing a progress bar forever, and it must
        // not escape the command either - an unhandled exception out of an async command has
        // nowhere useful to go. It is reported as a failed task instead.
        var sanitizer = new Mock<ILibrarySanitizerService>();
        sanitizer.Setup(x => x.SanitizeAsync(It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("disk gone"));

        var vm = new MaintenanceSectionViewModel(
            _host, BuildScopeFactory(s => s.AddScoped(_ => sanitizer.Object)), _tasks);

        await vm.RepairLibraryCommand.ExecuteAsync(null);

        Assert.Equal(new[] { true, false }, _host.RepairingChanges);
        Assert.False(vm.IsRepairRunning);
        Assert.Equal(BackgroundTaskState.Failed, _tasks.Single.State);
        Assert.Equal("disk gone", _tasks.Single.CompletionMessage);
    }

    [Fact]
    public async Task Maintenance_AnnouncesTheRepairAsACancellableTask()
    {
        // The plan's complaint about the old startup pass was that it was uncancellable. The pass
        // always threaded a token; nothing was ever passing one.
        var sanitizer = new Mock<ILibrarySanitizerService>();
        var vm = new MaintenanceSectionViewModel(
            _host, BuildScopeFactory(s => s.AddScoped(_ => sanitizer.Object)), _tasks);

        await vm.RepairLibraryCommand.ExecuteAsync(null);

        Assert.Equal("Repairing library", _tasks.Single.Title);
        Assert.Equal("Settings", _tasks.Single.OwnerNavLabel);
        Assert.Equal(BackgroundTaskState.Completed, _tasks.Single.State);
    }

    [Fact]
    public async Task Maintenance_KeepsTheSummaryTheRepairReturnedRatherThanItsLastProgressLine()
    {
        // Progress<T> posts asynchronously, so scraping the last reported line kept whichever
        // message happened to have landed - in practice "Confirming 2 possible duplicates..."
        // rather than the summary. The summary comes back from the call itself.
        var sanitizer = new Mock<ILibrarySanitizerService>();
        sanitizer.Setup(x => x.SanitizeAsync(It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
                 .Callback<IProgress<string>?, CancellationToken>((p, _) => p?.Report("Confirming 2 possible duplicates..."))
                 .ReturnsAsync("Sanitize complete: 1 repair(s), 0 orphan(s) removed.");

        var vm = new MaintenanceSectionViewModel(
            _host, BuildScopeFactory(s => s.AddScoped(_ => sanitizer.Object)), _tasks);

        await vm.RepairLibraryCommand.ExecuteAsync(null);

        Assert.Equal("Sanitize complete: 1 repair(s), 0 orphan(s) removed.", _tasks.Single.CompletionMessage);
    }

    [Fact]
    public async Task Maintenance_ReportsACancelledRepairAsCancelled()
    {
        // Every repair already made is a completed write of its own, so stopping leaves the library
        // consistent - just less repaired. The message has to say that rather than read as failure.
        var sanitizer = new Mock<ILibrarySanitizerService>();
        sanitizer.Setup(x => x.SanitizeAsync(It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new OperationCanceledException());

        var vm = new MaintenanceSectionViewModel(
            _host, BuildScopeFactory(s => s.AddScoped(_ => sanitizer.Object)), _tasks);

        await vm.RepairLibraryCommand.ExecuteAsync(null);

        Assert.Equal(BackgroundTaskState.Cancelled, _tasks.Single.State);
        Assert.Contains("already repaired was kept", _tasks.Single.CompletionMessage);
        Assert.False(vm.IsRepairRunning);
    }

    // ---- Titles ----

    [Fact]
    public void EverySectionNamesItself()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(x => x.Current).Returns(new AppSettings());

        SettingsSectionViewModel[] sections =
        [
            new PreferencesSectionViewModel(_host, BuildScopeFactory()),
            new TranscriptionSectionViewModel(_host, settings.Object),
            new ObsIntegrationSectionViewModel(_host),
            new MaintenanceSectionViewModel(_host, BuildScopeFactory(), _tasks),
            new AboutSectionViewModel(_host, new FakeApplicationUpdateService()),
        ];

        Assert.All(sections, s => Assert.False(string.IsNullOrWhiteSpace(s.Title)));
    }
}
