using ClipStudio.Application.Services;
using ClipStudio.Tests.Fakes;

namespace ClipStudio.Tests.Application;

/// <summary>
/// Covers the two hashes duplicate detection relies on: a quick screening hash that reads only the
/// ends of a file, and a full hash used to confirm a suspected match.
/// </summary>
public class FileHashServiceTests
{
    private readonly FakeFileSystem _fileSystem = new();

    /// <summary>Builds a service with a small chunk size so short fixtures still split head/tail.</summary>
    /// <param name="chunkSize">Bytes read from each end of the file.</param>
    private FileHashService Create(int chunkSize = 4) => new(_fileSystem, chunkSize);

    /// <summary>Adds a file whose contents are the given text.</summary>
    private void File(string path, string contents) => _fileSystem.AddFile(path, contents: contents);

    // ---- Quick hash ----

    [Fact]
    public async Task QuickHashIsStableAcrossCalls()
    {
        File("/clips/a.mp4", "HEADERmiddleFOOTER");
        var service = Create();

        var first  = await service.ComputeQuickHashAsync("/clips/a.mp4");
        var second = await service.ComputeQuickHashAsync("/clips/a.mp4");

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task IdenticalContentsHashTheSame()
    {
        File("/clips/a.mp4", "HEADERmiddleFOOTER");
        File("/clips/b.mp4", "HEADERmiddleFOOTER");
        var service = Create();

        Assert.Equal(
            await service.ComputeQuickHashAsync("/clips/a.mp4"),
            await service.ComputeQuickHashAsync("/clips/b.mp4"));
    }

    [Fact]
    public async Task ADifferentStartChangesTheQuickHash()
    {
        File("/clips/a.mp4", "HEADERmiddleFOOTER");
        File("/clips/b.mp4", "XEADERmiddleFOOTER");
        var service = Create();

        Assert.NotEqual(
            await service.ComputeQuickHashAsync("/clips/a.mp4"),
            await service.ComputeQuickHashAsync("/clips/b.mp4"));
    }

    [Fact]
    public async Task ADifferentEndChangesTheQuickHash()
    {
        // The reason the tail is hashed at all: re-encodes of one source often differ only here.
        File("/clips/a.mp4", "HEADERmiddleFOOTER");
        File("/clips/b.mp4", "HEADERmiddleFOOTEX");
        var service = Create();

        Assert.NotEqual(
            await service.ComputeQuickHashAsync("/clips/a.mp4"),
            await service.ComputeQuickHashAsync("/clips/b.mp4"));
    }

    [Fact]
    public async Task ADifferentLengthChangesTheQuickHashEvenWhenBothEndsMatch()
    {
        // The reason the length is mixed in: with a 4-byte chunk these share their first and last
        // four bytes, so without the length a truncated file would screen as identical.
        File("/clips/long.mp4",  "HEADlonglongmiddleFOOT");
        File("/clips/short.mp4", "HEADFOOT");
        var service = Create();

        Assert.NotEqual(
            await service.ComputeQuickHashAsync("/clips/long.mp4"),
            await service.ComputeQuickHashAsync("/clips/short.mp4"));
    }

    [Fact]
    public async Task AChangeInTheMiddleOfALargeFileIsNotSeenByTheQuickHash()
    {
        // Documents the trade the quick hash makes deliberately: it screens, it does not confirm.
        // Both files share length, head and tail, so only the full hash can tell them apart.
        File("/clips/a.mp4", "HEADaaaaaaaaaaaaFOOT");
        File("/clips/b.mp4", "HEADbbbbbbbbbbbbFOOT");
        var service = Create();

        Assert.Equal(
            await service.ComputeQuickHashAsync("/clips/a.mp4"),
            await service.ComputeQuickHashAsync("/clips/b.mp4"));

        Assert.NotEqual(
            await service.ComputeFullHashAsync("/clips/a.mp4"),
            await service.ComputeFullHashAsync("/clips/b.mp4"));
    }

    [Fact]
    public async Task AFileSmallerThanTwoChunksIsHashedWhole()
    {
        // Below the two-chunk threshold the ranges would overlap, so the quick hash reads
        // everything - which makes it conclusive for short files.
        File("/clips/a.mp4", "abcXdef");
        File("/clips/b.mp4", "abcYdef");
        var service = Create(chunkSize: 8);

        Assert.NotEqual(
            await service.ComputeQuickHashAsync("/clips/a.mp4"),
            await service.ComputeQuickHashAsync("/clips/b.mp4"));
    }

    [Fact]
    public async Task AnEmptyFileHashesWithoutFailing()
    {
        File("/clips/empty.mp4", string.Empty);
        var service = Create();

        var quick = await service.ComputeQuickHashAsync("/clips/empty.mp4");

        Assert.False(string.IsNullOrWhiteSpace(quick));
    }

    // ---- Full hash ----

    [Fact]
    public async Task FullHashMatchesTheKnownSha256OfTheContents()
    {
        // Pinned against a known value so a change of algorithm or encoding cannot pass silently.
        File("/clips/a.mp4", "abc");
        var service = Create();

        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            await service.ComputeFullHashAsync("/clips/a.mp4"));
    }

    [Fact]
    public async Task FullHashDiffersFromQuickHashForTheSameFile()
    {
        // They must never be compared against each other, so they must not collide by construction.
        File("/clips/a.mp4", "HEADERmiddleFOOTER");
        var service = Create();

        Assert.NotEqual(
            await service.ComputeQuickHashAsync("/clips/a.mp4"),
            await service.ComputeFullHashAsync("/clips/a.mp4"));
    }

    [Fact]
    public async Task HashesAreLowercaseHex()
    {
        File("/clips/a.mp4", "HEADERmiddleFOOTER");
        var service = Create();

        var quick = await service.ComputeQuickHashAsync("/clips/a.mp4");

        Assert.Equal(64, quick.Length);
        Assert.All(quick, c => Assert.True(char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f')));
    }

    // ---- Cancellation and arguments ----

    [Fact]
    public async Task ACancelledTokenStopsAQuickHash()
    {
        File("/clips/a.mp4", new string('x', 4096));
        var service = Create();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ComputeQuickHashAsync("/clips/a.mp4", cts.Token));
    }

    [Fact]
    public void ANonPositiveChunkSizeIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileHashService(_fileSystem, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileHashService(_fileSystem, -1));
    }
}
