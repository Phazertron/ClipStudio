using ClipStudio.Application.Models;
using ClipStudio.Application.Services;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;
using ClipStudio.Core.Models;
using ClipStudio.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClipStudio.Tests.Application;

/// <summary>
/// Unit tests for <see cref="LibraryHealthCheckService"/>, the cheap pass that replaced the full
/// sanitize on startup.
/// </summary>
/// <remarks>
/// The behaviour worth pinning down is what the check refuses to do. It marks a clip broken only
/// when its folder is reachable and the file is genuinely absent; an unreachable folder leaves
/// every one of its clips exactly as it found them, which is what stops a library on an unplugged
/// drive coming back as hundreds of broken clips.
/// </remarks>
public sealed class LibraryHealthCheckServiceTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly Mock<IClipRepository> _clips = new();
    private readonly Mock<ISourceFolderRepository> _folders = new();

    private LibraryHealthCheckService BuildService()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _clips.Object);
        services.AddScoped(_ => _folders.Object);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new LibraryHealthCheckService(scopeFactory, _fs, NullLogger<LibraryHealthCheckService>.Instance);
    }

    private void Setup(IEnumerable<SourceFolder> folders, IEnumerable<ClipFileSnapshot> clips)
    {
        _folders.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(folders.ToList());
        _clips.Setup(x => x.GetFileSnapshotsAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(clips.ToList());
    }

    private static ClipFileSnapshot ClipAt(int id, int folderId, string path, bool isBroken = false)
        => new(id, folderId, path, System.IO.Path.GetFileName(path), isBroken);

    /// <summary>Asserts that the broken flag was never touched, for any clip.</summary>
    private void VerifyNoBrokenFlagWrites() =>
        _clips.Verify(
            x => x.SetBrokenAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);

    [Fact]
    public async Task ReportsNothingWhenEveryFileIsWhereItShouldBe()
    {
        _fs.AddFile("/clips/a.mp4");
        Setup([new SourceFolder { Id = 1, Path = "/clips" }], [ClipAt(1, 1, "/clips/a.mp4")]);

        var report = await BuildService().CheckAsync();

        Assert.False(report.NeedsAttention);
        Assert.Equal(1, report.ClipsChecked);
        Assert.Equal(0, report.ClipsMarkedBroken);
        VerifyNoBrokenFlagWrites();
    }

    [Fact]
    public async Task MarksAClipBrokenWhenItsFileIsGoneFromAReachableFolder()
    {
        _fs.CreateDirectory("/clips");
        Setup([new SourceFolder { Id = 1, Path = "/clips" }], [ClipAt(1, 1, "/clips/a.mp4")]);

        var report = await BuildService().CheckAsync();

        _clips.Verify(x => x.SetBrokenAsync(1, true, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, report.ClipsMarkedBroken);
        Assert.Single(report.Findings, f => f.Kind == LibraryHealthFindingKind.ClipFileMissing && f.ClipId == 1);
    }

    [Fact]
    public async Task ClearsAStaleBrokenFlagWhenTheFileIsBack()
    {
        _fs.AddFile("/clips/a.mp4");
        Setup([new SourceFolder { Id = 1, Path = "/clips" }], [ClipAt(1, 1, "/clips/a.mp4", isBroken: true)]);

        var report = await BuildService().CheckAsync();

        _clips.Verify(x => x.SetBrokenAsync(1, false, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, report.BrokenFlagsCleared);
        Assert.False(report.NeedsAttention);
    }

    [Fact]
    public async Task LeavesEveryClipAloneWhenTheSourceFolderCannotBeReached()
    {
        // The point of the whole split: a disconnected drive must not turn into a library full of
        // broken clips that the user then has to un-break by hand.
        Setup(
            [new SourceFolder { Id = 1, Path = "/removable" }],
            [ClipAt(1, 1, "/removable/a.mp4"), ClipAt(2, 1, "/removable/b.mp4")]);

        var report = await BuildService().CheckAsync();

        Assert.Equal(0, report.ClipsMarkedBroken);
        Assert.Equal(2, report.ClipsSkippedAsUnreachable);
        VerifyNoBrokenFlagWrites();

        var finding = Assert.Single(report.Findings);
        Assert.Equal(LibraryHealthFindingKind.SourceFolderUnreachable, finding.Kind);
        Assert.Equal(2, finding.Count);
    }

    [Fact]
    public async Task ReportsFilesThatNoClipRefersTo()
    {
        _fs.AddFile("/clips/known.mp4");
        _fs.AddFile("/clips/new-one.mp4");
        _fs.AddFile("/clips/new-two.mp4");
        Setup([new SourceFolder { Id = 1, Path = "/clips" }], [ClipAt(1, 1, "/clips/known.mp4")]);

        var report = await BuildService().CheckAsync();

        var finding = Assert.Single(report.Findings);
        Assert.Equal(LibraryHealthFindingKind.UnimportedFilesFound, finding.Kind);
        Assert.Equal(2, finding.Count);
        Assert.Contains("scan is recommended", finding.Summary);
    }

    [Fact]
    public async Task IgnoresFilesThatAreNotVideos()
    {
        // The listing is compared using the same rule the import scan uses, or every stray text
        // file in a source folder would read as something waiting to be imported.
        _fs.AddFile("/clips/known.mp4");
        _fs.AddFile("/clips/notes.txt");
        _fs.AddFile("/clips/cover.png");
        Setup([new SourceFolder { Id = 1, Path = "/clips" }], [ClipAt(1, 1, "/clips/known.mp4")]);

        var report = await BuildService().CheckAsync();

        Assert.False(report.NeedsAttention);
    }

    [Fact]
    public async Task KeepsTheLastReportForWhateverPresentsIt()
    {
        _fs.AddFile("/clips/a.mp4");
        Setup([new SourceFolder { Id = 1, Path = "/clips" }], [ClipAt(1, 1, "/clips/a.mp4")]);

        var service = BuildService();
        Assert.Null(service.LastReport);

        var report = await service.CheckAsync();

        Assert.Same(report, service.LastReport);
    }

    [Fact]
    public async Task ReadsOnlyTheColumnsItNeeds()
    {
        // Loading whole clips would pull every tag, player and highlight with them, which is one of
        // the two costs that made the old startup pass slow.
        _fs.AddFile("/clips/a.mp4");
        Setup([new SourceFolder { Id = 1, Path = "/clips" }], [ClipAt(1, 1, "/clips/a.mp4")]);

        await BuildService().CheckAsync();

        _clips.Verify(x => x.GetFileSnapshotsAsync(It.IsAny<CancellationToken>()), Times.Once);
        _clips.Verify(x => x.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReportsProgressPerFolder()
    {
        _fs.AddFile("/one/a.mp4");
        _fs.AddFile("/two/b.mp4");
        Setup(
            [new SourceFolder { Id = 1, Path = "/one" }, new SourceFolder { Id = 2, Path = "/two" }],
            [ClipAt(1, 1, "/one/a.mp4"), ClipAt(2, 2, "/two/b.mp4")]);

        var messages = new List<string>();
        await BuildService().CheckAsync(new Progress<string>(messages.Add));

        // Progress is posted synchronously here because there is no synchronisation context in a
        // test, so the messages are already in the list.
        Assert.Contains(messages, m => m.Contains("/one"));
        Assert.Contains(messages, m => m.Contains("/two"));
    }
}
