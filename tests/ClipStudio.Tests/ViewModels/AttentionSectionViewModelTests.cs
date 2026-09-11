using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Interfaces;
using ClipStudio.Core.Models;
using ClipStudio.Tests.Fakes;
using ClipStudio.UI.Services;
using ClipStudio.UI.ViewModels;
using ClipStudio.UI.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ClipStudio.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="AttentionSectionViewModel"/>, the one list of everything the library
/// needs a person to decide about.
/// </summary>
/// <remarks>
/// The rule under test throughout is that the section reports and offers, and never resolves: no
/// entry writes to the library, and the action each one carries belongs to somewhere that already
/// exists.
/// </remarks>
public sealed class AttentionSectionViewModelTests
{
    private readonly FakeSettingsSectionHost _host = new();
    private readonly FakeAttentionActionHost _actions = new();
    private readonly FakeBackgroundTaskService _tasks = new();
    private readonly Mock<IClipRepository> _clips = new();
    private readonly Mock<IHighlightRepository> _highlights = new();
    private readonly Mock<ILibraryHealthCheckService> _health = new();
    private readonly Mock<IDuplicateClipFinder> _duplicateFinder = new();
    private readonly FakeApplicationUpdateService _updates = new();

    public AttentionSectionViewModelTests()
    {
        _clips.Setup(x => x.GetFileSnapshotsAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<ClipFileSnapshot>());
        _highlights.Setup(x => x.GetRangeSnapshotsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<HighlightRangeSnapshot>());
        _duplicateFinder.Setup(x => x.LastGroups).Returns(new List<DuplicateClipGroup>());
    }

    private AttentionSectionViewModel BuildSection()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _clips.Object);
        services.AddScoped(_ => _highlights.Object);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new AttentionSectionViewModel(
            _host, scopeFactory, _health.Object, _duplicateFinder.Object, _actions, _tasks, _updates);
    }

    private void ReportWith(params LibraryHealthFinding[] findings) =>
        _health.Setup(x => x.LastReport).Returns(new LibraryHealthReport { Findings = findings });

    private static ClipFileSnapshot BrokenClip(int id, string path)
        => new(id, 1, path, System.IO.Path.GetFileName(path), IsBroken: true);

    // ---- Application updates ----

    [Fact]
    public async Task ListsNoUpdateEntryWhenTheBuildCannotUpdateItself()
    {
        var vm = BuildSection();

        await vm.RefreshAsync();

        Assert.DoesNotContain(vm.Entries, e => e.Kind == AttentionEntryKind.UpdateAvailable);
    }

    [Fact]
    public async Task OffersTheDownloadRatherThanTakingIt()
    {
        _updates.ReportAvailable("1.2.0", currentVersion: "1.1.1");

        var vm = BuildSection();
        await vm.RefreshAsync();

        var entry = Assert.Single(vm.Entries, e => e.Kind == AttentionEntryKind.UpdateAvailable);
        Assert.Contains("available", entry.Title);
        Assert.Equal("Download update", entry.ActionLabel);

        // Nothing may be downloaded until the user asks for it.
        Assert.Equal(0, _updates.DownloadCount);
    }

    [Fact]
    public async Task DownloadsOnlyWhenTheEntryIsActedOn()
    {
        _updates.ReportAvailable("1.2.0");

        var vm = BuildSection();
        await vm.RefreshAsync();

        var entry = Assert.Single(vm.Entries, e => e.Kind == AttentionEntryKind.UpdateAvailable);
        await entry.ActionCommand!.ExecuteAsync(null);

        Assert.Equal(1, _updates.DownloadCount);

        // Having arrived, the offer becomes an install rather than a second download.
        var after = Assert.Single(vm.Entries, e => e.Kind == AttentionEntryKind.UpdateAvailable);
        Assert.Equal("Restart and install", after.ActionLabel);
    }

    [Fact]
    public async Task ReportsTheDownloadIntoTheTaskRegistryWithProgress()
    {
        _updates.ReportAvailable("1.2.0");
        _updates.DownloadProgressSteps = [25, 50, 100];

        var vm = BuildSection();
        await vm.RefreshAsync();
        await vm.Entries.Single(e => e.Kind == AttentionEntryKind.UpdateAvailable)
                .ActionCommand!.ExecuteAsync(null);

        var task = Assert.Single(_tasks.Started, t => t.Title.Contains("Downloading ClipStudio"));
        Assert.Equal(BackgroundTaskState.Completed, task.State);
        Assert.Contains("1.2.0", task.Title);
    }

    [Fact]
    public async Task TheDownloadTaskCanBeCancelledWhileItRuns()
    {
        _updates.ReportAvailable("1.2.0");

        var cancellableWhileRunning = false;
        _updates.DuringDownload = () =>
            cancellableWhileRunning = _tasks.Started[0].CanCancel;

        var vm = BuildSection();
        await vm.RefreshAsync();
        await vm.Entries.Single(e => e.Kind == AttentionEntryKind.UpdateAvailable)
                .ActionCommand!.ExecuteAsync(null);

        Assert.True(cancellableWhileRunning, "The download task must offer a working cancel.");
    }

    [Fact]
    public async Task ACancelledDownloadLeavesTheOfferStanding()
    {
        _updates.ReportAvailable("1.2.0");
        _updates.DownloadCancels = true;

        var vm = BuildSection();
        await vm.RefreshAsync();
        await vm.Entries.Single(e => e.Kind == AttentionEntryKind.UpdateAvailable)
                .ActionCommand!.ExecuteAsync(null);

        // Cancelling is not refusing: the update is still there to be taken later.
        var entry = Assert.Single(vm.Entries, e => e.Kind == AttentionEntryKind.UpdateAvailable);
        Assert.Equal("Download update", entry.ActionLabel);
    }

    [Fact]
    public async Task ShowsNoActionWhileTheDownloadIsRunning()
    {
        _updates.ReportDownloading("1.2.0");

        var vm = BuildSection();
        await vm.RefreshAsync();

        // The task panel owns the progress bar and the cancel button; a second set of controls
        // here would be two ways to drive one operation.
        var entry = Assert.Single(vm.Entries, e => e.Kind == AttentionEntryKind.UpdateAvailable);
        Assert.Contains("Downloading", entry.Title);
        Assert.False(entry.HasAction);
    }

    [Fact]
    public async Task AnnouncesADownloadedUpdateAndNamesBothVersions()
    {
        _updates.ReportDownloaded("1.2.0", currentVersion: "1.1.1");

        var vm = BuildSection();
        await vm.RefreshAsync();

        var entry = Assert.Single(vm.Entries, e => e.Kind == AttentionEntryKind.UpdateAvailable);
        Assert.Contains("1.2.0", entry.Title);
        Assert.Contains("1.1.1", entry.Detail);
        Assert.True(entry.HasAction);
        Assert.Equal(1, vm.EntryCount);
        Assert.False(vm.IsEverythingFine);
    }

    [Fact]
    public async Task DoesNotInstallAnUpdateUntilTheEntryIsActedOn()
    {
        _updates.ReportDownloaded("1.2.0");

        var vm = BuildSection();
        await vm.RefreshAsync();

        Assert.Equal(0, _updates.ApplyCount);

        var entry = Assert.Single(vm.Entries, e => e.Kind == AttentionEntryKind.UpdateAvailable);
        await entry.ActionCommand!.ExecuteAsync(null);

        Assert.Equal(1, _updates.ApplyCount);
    }

    [Fact]
    public async Task ListsTheUpdateAboveTheLibraryFindings()
    {
        _updates.ReportDownloaded("1.2.0");
        ReportWith(new LibraryHealthFinding(
            LibraryHealthFindingKind.SourceFolderUnreachable,
            "E:\\Clips is not reachable."));

        var vm = BuildSection();
        await vm.RefreshAsync();

        Assert.Equal(AttentionEntryKind.UpdateAvailable, vm.Entries[0].Kind);
    }

    [Fact]
    public async Task RecheckLooksForAnUpdateAsWellAsScanningTheFolders()
    {
        var vm = BuildSection();

        await vm.RecheckCommand.ExecuteAsync(null);

        _health.Verify(h => h.CheckAsync(It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, _updates.CheckCount);
    }

    [Fact]
    public async Task RecheckStillRebuildsWhenTheUpdateCheckIsUnavailable()
    {
        // No network, or a build that cannot update itself. The folder findings must still be
        // refreshed rather than lost behind an update failure.
        _updates.SetStatus(UpdateStatus.Unsupported);
        ReportWith(new LibraryHealthFinding(
            LibraryHealthFindingKind.SourceFolderUnreachable,
            "E:\\Clips is not reachable."));

        var vm = BuildSection();
        await vm.RecheckCommand.ExecuteAsync(null);

        Assert.Contains(vm.Entries, e => e.Kind == AttentionEntryKind.SourceFolderUnreachable);
    }

    // ---- Empty ----

    [Fact]
    public async Task SaysNothingIsWrongWhenNothingIs()
    {
        var vm = BuildSection();

        await vm.RefreshAsync();

        Assert.Empty(vm.Entries);
        Assert.Equal(0, vm.EntryCount);
        Assert.True(vm.IsEverythingFine);
    }

    [Fact]
    public async Task SurvivesHavingNoHealthReportYet()
    {
        // The first launch after an upgrade can reach the settings page before a check has run.
        _health.Setup(x => x.LastReport).Returns((LibraryHealthReport?)null);

        var vm = BuildSection();
        await vm.RefreshAsync();

        Assert.True(vm.IsEverythingFine);
    }

    // ---- Folder findings ----

    [Fact]
    public async Task ListsAnUnreachableSourceFolderAndSendsTheUserToTheControls()
    {
        // There is no single right answer to an unplugged drive - reconnect it, archive its clips,
        // or drop the folder - so the entry must not choose one.
        ReportWith(new LibraryHealthFinding(
            LibraryHealthFindingKind.SourceFolderUnreachable,
            "Source folder 'E:/clips' cannot be reached. 112 clips are unavailable.",
            SourceFolderId: 1, Path: "E:/clips", Count: 112));

        var vm = BuildSection();
        await vm.RefreshAsync();

        var entry = Assert.Single(vm.Entries);
        Assert.Equal(AttentionEntryKind.SourceFolderUnreachable, entry.Kind);
        Assert.Contains("112 clips", entry.Detail);
        Assert.True(entry.HasAction);

        await entry.ActionCommand!.ExecuteAsync(null);
        Assert.Equal(1, _actions.ShowSourceFoldersCalls);
    }

    [Fact]
    public async Task OffersAScanForFilesThatHaveNotBeenImported()
    {
        ReportWith(new LibraryHealthFinding(
            LibraryHealthFindingKind.UnimportedFilesFound,
            "3 files in 'E:/clips' have not been imported. A scan is recommended.",
            SourceFolderId: 7, Path: "E:/clips", Count: 3));

        var vm = BuildSection();
        await vm.RefreshAsync();

        var entry = Assert.Single(vm.Entries);
        Assert.Equal("3 files have not been imported", entry.Title);

        await entry.ActionCommand!.ExecuteAsync(null);
        Assert.Equal([7], _actions.ScannedFolderIds);
    }

    [Fact]
    public async Task DoesNotDuplicateMissingFilesFromTheReport()
    {
        // The report's missing-file finding and the clip's broken flag are the same fact. The flag
        // is the current one, so a clip relocated since the check must not still be listed.
        ReportWith(new LibraryHealthFinding(
            LibraryHealthFindingKind.ClipFileMissing,
            "'a.mp4' is no longer at E:/clips/a.mp4.",
            SourceFolderId: 1, ClipId: 1, Path: "E:/clips/a.mp4"));

        var vm = BuildSection();
        await vm.RefreshAsync();

        Assert.Empty(vm.Entries);
    }

    // ---- Broken clips ----

    [Fact]
    public async Task ListsBrokenClipsIndividuallyAndOpensTheClip()
    {
        _clips.Setup(x => x.GetFileSnapshotsAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync([BrokenClip(4, "E:/clips/gone.mp4")]);

        var vm = BuildSection();
        await vm.RefreshAsync();

        var entry = Assert.Single(vm.Entries);
        Assert.Equal(AttentionEntryKind.ClipFileMissing, entry.Kind);
        Assert.Contains("gone.mp4", entry.Title);

        await entry.ActionCommand!.ExecuteAsync(null);
        Assert.Equal([4], _actions.OpenedClipIds);
    }

    [Fact]
    public async Task CollapsesALargeNumberOfBrokenClipsIntoOneEntry()
    {
        // A library that lost a whole folder would otherwise bury every other finding.
        _clips.Setup(x => x.GetFileSnapshotsAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(Enumerable.Range(1, 40)
                  .Select(i => BrokenClip(i, $"E:/clips/{i}.mp4"))
                  .ToList());

        var vm = BuildSection();
        await vm.RefreshAsync();

        var entry = Assert.Single(vm.Entries);
        Assert.Equal("40 clips are missing their file", entry.Title);
        Assert.False(entry.HasAction);
    }

    // ---- Out-of-range highlights ----

    [Fact]
    public async Task ListsAHighlightThatStartsPastTheEndOfItsClip()
    {
        _highlights.Setup(x => x.GetRangeSnapshotsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync([new HighlightRangeSnapshot(
                       9, 3, "lucky escape", "clip.mp4",
                       TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3.5),
                       TimeSpan.FromSeconds(178))]);

        var vm = BuildSection();
        await vm.RefreshAsync();

        var entry = Assert.Single(vm.Entries);
        Assert.Equal(AttentionEntryKind.HighlightOutOfRange, entry.Kind);
        Assert.Contains("lucky escape", entry.Title);
        Assert.Contains("chosen by hand", entry.Detail);

        await entry.ActionCommand!.ExecuteAsync(null);
        Assert.Equal([3], _actions.OpenedClipIds);
    }

    [Fact]
    public async Task IgnoresAHighlightThatStillFitsItsClip()
    {
        _highlights.Setup(x => x.GetRangeSnapshotsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync([new HighlightRangeSnapshot(
                       9, 3, "fine", "clip.mp4",
                       TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20),
                       TimeSpan.FromSeconds(178))]);

        var vm = BuildSection();
        await vm.RefreshAsync();

        Assert.True(vm.IsEverythingFine);
    }

    [Fact]
    public async Task TreatsAHighlightThatOnlyOverrunsTheEndAsUsable()
    {
        // Overrunning the end is the case the sanitizer truncates, and watch mode clamps. Only a
        // range with nothing playable left needs a person.
        _highlights.Setup(x => x.GetRangeSnapshotsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync([new HighlightRangeSnapshot(
                       9, 3, "overruns", "clip.mp4",
                       TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(300),
                       TimeSpan.FromSeconds(178))]);

        var vm = BuildSection();
        await vm.RefreshAsync();

        Assert.True(vm.IsEverythingFine);
    }

    // ---- Duplicates ----

    [Fact]
    public async Task ListsWhatTheLastDuplicateScanFound()
    {
        // Detection used to run only at import, so two copies already in the library were never
        // compared again. This is where that finally shows up.
        _duplicateFinder.Setup(x => x.LastGroups)
                        .Returns([new DuplicateClipGroup("hash", [1, 2])]);

        var vm = BuildSection();
        await vm.RefreshAsync();

        var entry = Assert.Single(vm.Entries);
        Assert.Equal(AttentionEntryKind.DuplicateClips, entry.Kind);
        Assert.Equal("2 clips are the same recording", entry.Title);
    }

    [Fact]
    public async Task NeverHashesWhileBuildingTheList()
    {
        // Confirming a duplicate reads both files end to end, so it belongs to Repair Library. The
        // list only shows what that last run found.
        _duplicateFinder.Setup(x => x.LastGroups)
                        .Returns([new DuplicateClipGroup("hash", [1, 2, 3])]);

        var vm = BuildSection();
        await vm.RefreshAsync();

        _duplicateFinder.Verify(
            x => x.FindAsync(It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Equal("3 clips are the same recording", Assert.Single(vm.Entries).Title);
    }

    [Fact]
    public async Task ScansForDuplicatesOnDemandAndSaysWhatItFound()
    {
        // The groups live in memory, so after a restart nothing is listed until something looks
        // again. Making the user run a full repair for that would be a poor trade.
        _duplicateFinder.Setup(x => x.FindAsync(It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync([new DuplicateClipGroup("hash", [1, 2])])
                        .Callback(() => _duplicateFinder.Setup(x => x.LastGroups)
                                                        .Returns([new DuplicateClipGroup("hash", [1, 2])]));

        var vm = BuildSection();
        await vm.ScanForDuplicatesCommand.ExecuteAsync(null);

        Assert.Equal("Found 2 clips in 1 group(s).", vm.DuplicateScanResult);
        Assert.Single(vm.Entries, e => e.Kind == AttentionEntryKind.DuplicateClips);
        Assert.False(vm.IsScanningForDuplicates);
    }

    [Fact]
    public async Task ADuplicateScanAnnouncesItselfAsCancellableWork()
    {
        // It reads whole files to confirm a match, so it is the scan most worth being able to stop
        // and the one most worth seeing from another page while it runs.
        _duplicateFinder.Setup(x => x.FindAsync(It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync([]);

        var vm = BuildSection();
        await vm.ScanForDuplicatesCommand.ExecuteAsync(null);

        Assert.Equal("Looking for duplicate clips", _tasks.Single.Title);
        Assert.Equal("Settings", _tasks.Single.OwnerNavLabel);
        Assert.Equal(BackgroundTaskState.Completed, _tasks.Single.State);
        Assert.Equal("No duplicate clips found.", _tasks.Single.CompletionMessage);
    }

    [Fact]
    public async Task ACancelledDuplicateScanReportsNoGroupsRatherThanAPartialAnswer()
    {
        // A partial scan would list some groups and silently omit others, which reads as "these are
        // the duplicates" when it is not.
        _duplicateFinder.Setup(x => x.FindAsync(It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new OperationCanceledException());

        var vm = BuildSection();
        await vm.ScanForDuplicatesCommand.ExecuteAsync(null);

        Assert.Equal("Duplicate scan cancelled.", vm.DuplicateScanResult);
        Assert.Equal(BackgroundTaskState.Cancelled, _tasks.Single.State);
        Assert.False(vm.IsScanningForDuplicates);
    }

    [Fact]
    public async Task SaysSoWhenADuplicateScanFindsNothing()
    {
        _duplicateFinder.Setup(x => x.FindAsync(It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync([]);

        var vm = BuildSection();
        await vm.ScanForDuplicatesCommand.ExecuteAsync(null);

        Assert.Equal("No duplicate clips found.", vm.DuplicateScanResult);
        Assert.True(vm.IsEverythingFine);
    }

    [Fact]
    public async Task ADuplicateEntryOffersAMergeOnlyWhenThereIsSomewhereToShowIt()
    {
        _duplicateFinder.Setup(x => x.LastGroups)
                        .Returns([new DuplicateClipGroup("hash", [1, 2])]);

        var vm = BuildSection();
        await vm.RefreshAsync();
        Assert.False(Assert.Single(vm.Entries).HasAction);

        // With a window to show the dialog over, the same entry offers the merge.
        DuplicateClipGroup? merged = null;
        vm.MergeRequested = g => { merged = g; return Task.FromResult(true); };
        await vm.RefreshAsync();

        var entry = Assert.Single(vm.Entries);
        Assert.Equal("Inspect...", entry.ActionLabel);
        await entry.ActionCommand!.ExecuteAsync(null);
        Assert.Equal("hash", merged?.FullHash);
    }

    [Fact]
    public async Task ADuplicateEntryGainsItsActionOnceTheViewCanShowTheDialog()
    {
        // The startup rebuild happens before any view exists, so the entry is built with no merge
        // action. Once the view attaches and sets MergeRequested, rebuilding must give the entry
        // its button - otherwise a restored group shows as an unresolvable row.
        _duplicateFinder.Setup(x => x.LastGroups)
                        .Returns([new DuplicateClipGroup("hash", [1, 2])]);

        var vm = BuildSection();
        await vm.RebuildAsync();
        Assert.False(Assert.Single(vm.Entries).HasAction);

        vm.MergeRequested = _ => Task.FromResult(true);
        await vm.RebuildAsync();

        Assert.True(Assert.Single(vm.Entries).HasAction);
    }

    // ---- Counting ----

    [Fact]
    public async Task AnnouncesItsCountSoTheNavigationBadgeCanFollow()
    {
        ReportWith(new LibraryHealthFinding(
            LibraryHealthFindingKind.SourceFolderUnreachable, "gone", SourceFolderId: 1, Count: 2));
        _clips.Setup(x => x.GetFileSnapshotsAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync([BrokenClip(4, "E:/clips/gone.mp4")]);

        var counts = new List<int>();
        var vm = BuildSection();
        vm.EntryCountChanged = counts.Add;

        await vm.RefreshAsync();

        Assert.Equal(2, vm.EntryCount);
        Assert.Equal([2], counts);
        Assert.False(vm.IsEverythingFine);
    }

    [Fact]
    public async Task ARecheckRunsTheHealthCheckAgain()
    {
        var vm = BuildSection();

        await vm.RecheckCommand.ExecuteAsync(null);

        _health.Verify(x => x.CheckAsync(It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(vm.IsChecking);
    }

    [Fact]
    public async Task NeverWritesToTheLibrary()
    {
        // The whole section is a reader. Nothing here is auto-resolved.
        ReportWith(new LibraryHealthFinding(
            LibraryHealthFindingKind.UnimportedFilesFound, "files", SourceFolderId: 1, Count: 3));
        _clips.Setup(x => x.GetFileSnapshotsAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync([BrokenClip(4, "E:/clips/gone.mp4")]);
        _highlights.Setup(x => x.GetRangeSnapshotsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync([new HighlightRangeSnapshot(
                       9, 3, "bad", "clip.mp4",
                       TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3.5),
                       TimeSpan.FromSeconds(178))]);

        var vm = BuildSection();
        await vm.RefreshAsync();

        _clips.Verify(x => x.UpdateAsync(It.IsAny<ClipStudio.Core.Entities.Clip>(), It.IsAny<CancellationToken>()), Times.Never);
        _clips.Verify(x => x.SetBrokenAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _highlights.Verify(x => x.UpdateAsync(It.IsAny<ClipStudio.Core.Entities.Highlight>(), It.IsAny<CancellationToken>()), Times.Never);
        _highlights.Verify(x => x.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
