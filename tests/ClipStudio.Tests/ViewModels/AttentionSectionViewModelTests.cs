using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Interfaces;
using ClipStudio.Core.Models;
using ClipStudio.Tests.Fakes;
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
    private readonly Mock<IClipRepository> _clips = new();
    private readonly Mock<IHighlightRepository> _highlights = new();
    private readonly Mock<ILibraryHealthCheckService> _health = new();

    public AttentionSectionViewModelTests()
    {
        _clips.Setup(x => x.GetFileSnapshotsAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<ClipFileSnapshot>());
        _highlights.Setup(x => x.GetRangeSnapshotsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<HighlightRangeSnapshot>());
    }

    private AttentionSectionViewModel BuildSection()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _clips.Object);
        services.AddScoped(_ => _highlights.Object);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new AttentionSectionViewModel(_host, scopeFactory, _health.Object, _actions);
    }

    private void ReportWith(params LibraryHealthFinding[] findings) =>
        _health.Setup(x => x.LastReport).Returns(new LibraryHealthReport { Findings = findings });

    private static ClipFileSnapshot BrokenClip(int id, string path)
        => new(id, 1, path, System.IO.Path.GetFileName(path), IsBroken: true);

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
