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
}
