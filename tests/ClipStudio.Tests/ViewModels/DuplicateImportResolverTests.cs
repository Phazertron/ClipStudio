using ClipStudio.Application.Models;
using ClipStudio.Core.Entities;
using ClipStudio.UI.ViewModels;

namespace ClipStudio.Tests.ViewModels;

/// <summary>
/// Covers resolving the duplicates a scan turned up: what gets asked, what gets imported, and how
/// "do the same for the rest" short-circuits the remaining questions.
/// </summary>
public class DuplicateImportResolverTests
{
    private readonly List<DuplicateClipPrompt> _asked = [];
    private readonly List<string> _imported = [];

    /// <summary>Builds a duplicate result for a file that matches an existing clip.</summary>
    private static ImportResult Duplicate(string path, int existingId = 1)
        => ImportResult.Duplicate(path, new Clip
        {
            Id       = existingId,
            FilePath = $"/library/existing{existingId}.mp4",
            FileName = $"existing{existingId}.mp4",
        });

    /// <summary>Runs the resolver, answering every prompt the same way.</summary>
    private Task<DuplicateResolutionSummary> Resolve(
        IReadOnlyList<ImportResult> results, DuplicateResolution answer)
        => Resolve(results, _ => answer);

    /// <summary>Runs the resolver with a per-prompt answer.</summary>
    private Task<DuplicateResolutionSummary> Resolve(
        IReadOnlyList<ImportResult> results,
        Func<DuplicateClipPrompt, DuplicateResolution> answer)
        => DuplicateImportResolver.ResolveAsync(
            results,
            prompt =>
            {
                _asked.Add(prompt);
                return Task.FromResult(answer(prompt));
            },
            path =>
            {
                _imported.Add(path);
                return Task.FromResult(true);
            });

    [Fact]
    public async Task NoDuplicatesAsksNothing()
    {
        var summary = await Resolve([ImportResult.Skipped("already there")], DuplicateResolution.Skip);

        Assert.Empty(_asked);
        Assert.Equal(0, summary.Total);
    }

    [Fact]
    public async Task SkippingLeavesTheFileOutOfTheLibrary()
    {
        var summary = await Resolve([Duplicate("/clips/a.mp4")], DuplicateResolution.Skip);

        Assert.Empty(_imported);
        Assert.Equal(1, summary.Skipped);
        Assert.Equal(0, summary.Imported);
    }

    [Fact]
    public async Task ImportingAnywayImportsTheFile()
    {
        var summary = await Resolve([Duplicate("/clips/a.mp4")], DuplicateResolution.ImportAnyway);

        Assert.Equal(["/clips/a.mp4"], _imported);
        Assert.Equal(1, summary.Imported);
        Assert.Equal(0, summary.Skipped);
    }

    [Fact]
    public async Task EachDuplicateIsAskedAboutSeparatelyByDefault()
    {
        var results = new[] { Duplicate("/clips/a.mp4"), Duplicate("/clips/b.mp4", 2) };

        await Resolve(results, DuplicateResolution.Skip);

        Assert.Equal(2, _asked.Count);
    }

    [Fact]
    public async Task ApplyToRemainingStopsAskingAndSkipsTheRest()
    {
        // The reason the option exists: a scan can turn up dozens, and asking about each one is
        // its own problem.
        var results = new[]
        {
            Duplicate("/clips/a.mp4"), Duplicate("/clips/b.mp4", 2), Duplicate("/clips/c.mp4", 3),
        };

        var summary = await Resolve(
            results, new DuplicateResolution(DuplicateClipDecision.Skip, ApplyToRemaining: true));

        Assert.Single(_asked);
        Assert.Empty(_imported);
        Assert.Equal(3, summary.Skipped);
    }

    [Fact]
    public async Task ApplyToRemainingCarriesAnImportDecisionToo()
    {
        var results = new[] { Duplicate("/clips/a.mp4"), Duplicate("/clips/b.mp4", 2) };

        var summary = await Resolve(
            results,
            new DuplicateResolution(DuplicateClipDecision.ImportAnyway, ApplyToRemaining: true));

        Assert.Single(_asked);
        Assert.Equal(["/clips/a.mp4", "/clips/b.mp4"], _imported);
        Assert.Equal(2, summary.Imported);
    }

    [Fact]
    public async Task ApplyToRemainingOnTheLastOneChangesNothing()
    {
        var summary = await Resolve(
            [Duplicate("/clips/a.mp4")],
            new DuplicateResolution(DuplicateClipDecision.Skip, ApplyToRemaining: true));

        Assert.Single(_asked);
        Assert.Equal(1, summary.Skipped);
    }

    [Fact]
    public async Task DecisionsCanDifferPerDuplicate()
    {
        var results = new[] { Duplicate("/clips/keep.mp4"), Duplicate("/clips/drop.mp4", 2) };

        var summary = await Resolve(results, prompt =>
            prompt.IncomingFilePath.Contains("keep")
                ? DuplicateResolution.ImportAnyway
                : DuplicateResolution.Skip);

        Assert.Equal(["/clips/keep.mp4"], _imported);
        Assert.Equal(1, summary.Imported);
        Assert.Equal(1, summary.Skipped);
    }

    [Fact]
    public async Task ThePromptCountsWhatIsStillWaiting()
    {
        var results = new[]
        {
            Duplicate("/clips/a.mp4"), Duplicate("/clips/b.mp4", 2), Duplicate("/clips/c.mp4", 3),
        };

        await Resolve(results, DuplicateResolution.Skip);

        Assert.Equal([2, 1, 0], _asked.Select(p => p.RemainingCount));
    }

    [Fact]
    public async Task AFailedImportCountsAsSkippedRatherThanImported()
    {
        // The summary has to reflect what is actually in the library.
        var summary = await DuplicateImportResolver.ResolveAsync(
            [Duplicate("/clips/a.mp4")],
            _ => Task.FromResult(DuplicateResolution.ImportAnyway),
            _ => Task.FromResult(false));

        Assert.Equal(0, summary.Imported);
        Assert.Equal(1, summary.Skipped);
    }

    [Fact]
    public async Task OnlyDuplicatesAreConsidered()
    {
        var results = new ImportResult[]
        {
            ImportResult.Succeeded(new Clip { Id = 9 }),
            ImportResult.Failed("unreadable"),
            Duplicate("/clips/a.mp4"),
        };

        await Resolve(results, DuplicateResolution.Skip);

        Assert.Single(_asked);
        Assert.Equal("/clips/a.mp4", _asked[0].IncomingFilePath);
    }

    [Fact]
    public async Task CancellationStopsTheRemainingPrompts()
    {
        var results = new[] { Duplicate("/clips/a.mp4"), Duplicate("/clips/b.mp4", 2) };
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DuplicateImportResolver.ResolveAsync(
                results,
                prompt =>
                {
                    _asked.Add(prompt);
                    cts.Cancel();
                    return Task.FromResult(DuplicateResolution.Skip);
                },
                _ => Task.FromResult(true),
                cts.Token));

        Assert.Single(_asked);
    }
}
