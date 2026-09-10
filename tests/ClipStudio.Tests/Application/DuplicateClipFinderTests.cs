using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Services;
using ClipStudio.Core.Interfaces;
using ClipStudio.Core.Models;
using ClipStudio.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClipStudio.Tests.Application;

/// <summary>
/// Unit tests for <see cref="DuplicateClipFinder"/>, which closes the gap where two copies already
/// in the library were never compared because detection only ran at import.
/// </summary>
public sealed class DuplicateClipFinderTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly Mock<IClipRepository> _clips = new();
    private readonly Mock<IFileHashService> _hashes = new();

    private DuplicateClipFinder BuildFinder()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _clips.Object);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new DuplicateClipFinder(
            scopeFactory, _hashes.Object, _fs, NullLogger<DuplicateClipFinder>.Instance);
    }

    private static ClipFileSnapshot Clip(int id, string path, string? quickHash)
        => new(id, 1, path, System.IO.Path.GetFileName(path), IsBroken: false, FileHash: quickHash);

    private void Library(params ClipFileSnapshot[] clips)
    {
        _clips.Setup(x => x.GetFileSnapshotsAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(clips.ToList());

        foreach (var clip in clips)
            _fs.AddFile(clip.FilePath);
    }

    private void FullHash(string path, string hash) =>
        _hashes.Setup(x => x.ComputeFullHashAsync(path, It.IsAny<CancellationToken>()))
               .ReturnsAsync(hash);

    [Fact]
    public async Task FindsNothingInALibraryWithNoRepeatedQuickHash()
    {
        Library(Clip(1, "/clips/a.mp4", "aaa"), Clip(2, "/clips/b.mp4", "bbb"));

        var groups = await BuildFinder().FindAsync();

        Assert.Empty(groups);
        _hashes.Verify(
            x => x.ComputeFullHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GroupsTwoClipsWhoseContentsAreIdentical()
    {
        // The case testing hit on 2026-09-09: a clip and a copy of it under a different name, both
        // in the library, and nothing ever reporting them.
        Library(
            Clip(1, "/clips/warframe.mp4", "quick"),
            Clip(2, "/clips/DUPLICATE TEST copy.mp4", "quick"));
        FullHash("/clips/warframe.mp4", "same");
        FullHash("/clips/DUPLICATE TEST copy.mp4", "same");

        var groups = await BuildFinder().FindAsync();

        var group = Assert.Single(groups);
        Assert.Equal("same", group.FullHash);
        Assert.Equal([1, 2], group.ClipIds);
    }

    [Fact]
    public async Task RejectsAQuickHashCollisionBetweenDifferentRecordings()
    {
        // The quick hash only screens. Two different files sharing one must not be called a
        // duplicate - that is the whole reason the full hash exists.
        Library(Clip(1, "/clips/a.mp4", "quick"), Clip(2, "/clips/b.mp4", "quick"));
        FullHash("/clips/a.mp4", "one");
        FullHash("/clips/b.mp4", "two");

        var groups = await BuildFinder().FindAsync();

        Assert.Empty(groups);
    }

    [Fact]
    public async Task SplitsABucketHoldingBothAMatchAndACollision()
    {
        Library(
            Clip(1, "/clips/a.mp4", "quick"),
            Clip(2, "/clips/b.mp4", "quick"),
            Clip(3, "/clips/c.mp4", "quick"));
        FullHash("/clips/a.mp4", "same");
        FullHash("/clips/b.mp4", "same");
        FullHash("/clips/c.mp4", "different");

        var groups = await BuildFinder().FindAsync();

        var group = Assert.Single(groups);
        Assert.Equal([1, 2], group.ClipIds);
    }

    [Fact]
    public async Task IgnoresClipsThatHaveNeverBeenHashed()
    {
        // A clip imported while hashing was off carries no hash and takes no part until a repair
        // backfills one - which is why the sanitizer hashes before running this.
        Library(Clip(1, "/clips/a.mp4", null), Clip(2, "/clips/b.mp4", null));

        Assert.Empty(await BuildFinder().FindAsync());
    }

    [Fact]
    public async Task PassesOverACandidateWhoseFileIsMissing()
    {
        // Nothing to compare, and a stale row must not invent a duplicate.
        _clips.Setup(x => x.GetFileSnapshotsAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync([Clip(1, "/clips/a.mp4", "quick"), Clip(2, "/clips/gone.mp4", "quick")]);
        _fs.AddFile("/clips/a.mp4");

        var groups = await BuildFinder().FindAsync();

        Assert.Empty(groups);
        _hashes.Verify(
            x => x.ComputeFullHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LeavesOutAFileThatCannotBeReadRatherThanAssumingItMatches()
    {
        Library(
            Clip(1, "/clips/a.mp4", "quick"),
            Clip(2, "/clips/b.mp4", "quick"),
            Clip(3, "/clips/locked.mp4", "quick"));
        FullHash("/clips/a.mp4", "same");
        FullHash("/clips/b.mp4", "same");
        _hashes.Setup(x => x.ComputeFullHashAsync("/clips/locked.mp4", It.IsAny<CancellationToken>()))
               .ThrowsAsync(new IOException("in use"));

        var groups = await BuildFinder().FindAsync();

        var group = Assert.Single(groups);
        Assert.Equal([1, 2], group.ClipIds);
    }

    [Fact]
    public async Task GroupsThreeCopiesTogether()
    {
        Library(
            Clip(1, "/clips/a.mp4", "quick"),
            Clip(2, "/clips/b.mp4", "quick"),
            Clip(3, "/clips/c.mp4", "quick"));
        FullHash("/clips/a.mp4", "same");
        FullHash("/clips/b.mp4", "same");
        FullHash("/clips/c.mp4", "same");

        var group = Assert.Single(await BuildFinder().FindAsync());

        Assert.Equal(3, group.ClipIds.Count);
    }

    [Fact]
    public async Task KeepsTheResultForWhateverPresentsIt()
    {
        Library(Clip(1, "/clips/a.mp4", "quick"), Clip(2, "/clips/b.mp4", "quick"));
        FullHash("/clips/a.mp4", "same");
        FullHash("/clips/b.mp4", "same");

        var finder = BuildFinder();
        Assert.Empty(finder.LastGroups);
        Assert.Null(finder.LastRunUtc);

        await finder.FindAsync();

        Assert.Single(finder.LastGroups);
        Assert.NotNull(finder.LastRunUtc);

        finder.Forget("same");
        Assert.Empty(finder.LastGroups);
    }

    [Fact]
    public async Task ASecondRunReplacesTheFirstResult()
    {
        Library(Clip(1, "/clips/a.mp4", "quick"), Clip(2, "/clips/b.mp4", "quick"));
        FullHash("/clips/a.mp4", "same");
        FullHash("/clips/b.mp4", "same");

        var finder = BuildFinder();
        await finder.FindAsync();
        Assert.Single(finder.LastGroups);

        // The user resolved it; the next scan finds nothing and the stale group must not linger.
        _clips.Setup(x => x.GetFileSnapshotsAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync([Clip(1, "/clips/a.mp4", "quick")]);

        await finder.FindAsync();

        Assert.Empty(finder.LastGroups);
    }

    private static ClipFileSnapshot Confirmed(int id, string path, string contentHash)
        => new(id, 1, path, System.IO.Path.GetFileName(path), IsBroken: false,
               FileHash: "quick", ContentHash: contentHash);

    // ---- Surviving a restart ----

    [Fact]
    public async Task AScanStoresTheConfirmedHashesItPaidToCompute()
    {
        Library(
            Clip(1, "/clips/a.mp4", "quick"),
            Clip(2, "/clips/b.mp4", "quick"));
        FullHash("/clips/a.mp4", "same");
        FullHash("/clips/b.mp4", "same");

        await BuildFinder().FindAsync();

        // Without this the whole-file reads are thrown away when the app closes.
        _clips.Verify(x => x.SetContentHashesAsync(
            It.Is<IReadOnlyDictionary<int, string>>(d =>
                d.Count == 2 && d[1] == "same" && d[2] == "same"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestoresKnownGroupsWithoutReadingAnyFile()
    {
        Library(
            Confirmed(1, "/clips/a.mp4", "same"),
            Confirmed(2, "/clips/b.mp4", "same"));

        var groups = await BuildFinder().LoadKnownGroupsAsync();

        var group = Assert.Single(groups);
        Assert.Equal(new[] { 1, 2 }, group.ClipIds.OrderBy(i => i));

        // The point of storing the hash: no file is read to get this back.
        _hashes.Verify(
            x => x.ComputeFullHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ARestoredGroupIsExposedThroughLastGroups()
    {
        Library(
            Confirmed(1, "/clips/a.mp4", "same"),
            Confirmed(2, "/clips/b.mp4", "same"));

        var finder = BuildFinder();
        await finder.LoadKnownGroupsAsync();

        // This is what the attention list reads, so "nothing requires your attention" after a
        // restart was exactly this being empty.
        Assert.Single(finder.LastGroups);
    }

    [Fact]
    public async Task DoesNotRestoreAGroupWhoseOtherCopyIsGone()
    {
        // One of the pair was trashed or deleted, so the row no longer comes back. A stored group
        // would still claim a duplicate; a derived one simply stops existing.
        Library(Confirmed(1, "/clips/a.mp4", "same"));

        Assert.Empty(await BuildFinder().LoadKnownGroupsAsync());
    }

    [Fact]
    public async Task DoesNotRestoreAGroupWhoseFileHasSinceDisappeared()
    {
        _clips.Setup(x => x.GetFileSnapshotsAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync([
                  Confirmed(1, "/clips/a.mp4", "same"),
                  Confirmed(2, "/clips/gone.mp4", "same")]);

        // Only one of the two files is actually on disk.
        _fs.AddFile("/clips/a.mp4");

        Assert.Empty(await BuildFinder().LoadKnownGroupsAsync());
    }

    [Fact]
    public async Task RestoringReplacesWhateverWasHeldBefore()
    {
        Library(
            Confirmed(1, "/clips/a.mp4", "same"),
            Confirmed(2, "/clips/b.mp4", "same"));

        var finder = BuildFinder();
        await finder.LoadKnownGroupsAsync();
        Assert.Single(finder.LastGroups);

        Library(Confirmed(1, "/clips/a.mp4", "same"));
        await finder.LoadKnownGroupsAsync();

        Assert.Empty(finder.LastGroups);
    }
}
