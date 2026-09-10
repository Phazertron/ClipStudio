using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Entities;
using ClipStudio.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ClipStudio.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="DuplicateMergeViewModel"/>, which resolves one group of duplicate
/// clips.
/// </summary>
/// <remarks>
/// The files are identical, so every test here is about metadata: what survives, what has to be
/// asked for, and the order the writes happen in.
/// </remarks>
public sealed class DuplicateMergeViewModelTests
{
    private readonly Mock<IClipService> _clips = new();
    private readonly Mock<IPlayerService> _players = new();
    private readonly Mock<IHighlightService> _highlights = new();
    private readonly Mock<IDuplicateClipFinder> _finder = new();

    private DuplicateMergeViewModel Build(params int[] clipIds)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _clips.Object);
        services.AddScoped(_ => _players.Object);
        services.AddScoped(_ => _highlights.Object);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new DuplicateMergeViewModel(
            new DuplicateClipGroup("hash", clipIds), scopeFactory, _finder.Object);
    }

    /// <summary>Registers a clip with the given tags and highlights.</summary>
    private Clip SetUpClip(
        int id,
        string fileName,
        (int Id, string Name)[]? tags = null,
        Highlight[]? highlights = null,
        (int Id, string Name)[]? players = null)
    {
        var clip = new Clip
        {
            Id         = id,
            FileName   = fileName,
            FilePath   = $"/clips/{fileName}",
            ImportedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Highlights = highlights?.ToList() ?? [],
            ClipTags   = (tags ?? []).Select(t => new ClipTag
            {
                ClipId = id,
                TagId  = t.Id,
                Tag    = new Tag { Id = t.Id, Name = t.Name },
            }).ToList(),
        };

        foreach (var highlight in clip.Highlights)
            highlight.ClipId = id;

        _clips.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(clip);
        _players.Setup(x => x.GetByClipAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync((players ?? [])
                    .Select(p => new Player { Id = p.Id, DisplayName = p.Name })
                    .ToList());

        return clip;
    }

    private static Highlight Highlight(
        int id, int startSeconds, int endSeconds, string? label = null) => new()
    {
        Id        = id,
        Label     = label,
        StartTime = TimeSpan.FromSeconds(startSeconds),
        EndTime   = TimeSpan.FromSeconds(endSeconds),
    };

    // ---- Load ----

    [Fact]
    public async Task LoadsEveryCopyAndStartsWithTheFirstAsSurvivor()
    {
        SetUpClip(1, "original.mp4");
        SetUpClip(2, "copy.mp4");

        var vm = Build(1, 2);
        await vm.LoadAsync();

        Assert.Equal(2, vm.Candidates.Count);
        Assert.Same(vm.Candidates[0], vm.Survivor);
        Assert.True(vm.Candidates[0].IsSurvivor);
        Assert.False(vm.Candidates[1].IsSurvivor);
    }

    [Fact]
    public async Task SaysSoWhenTheGroupIsNoLongerADuplicate()
    {
        // A copy can be trashed or relocated between the scan and the merge.
        SetUpClip(1, "original.mp4");
        _clips.Setup(x => x.GetByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync((Clip?)null);

        var vm = Build(1, 2);
        await vm.LoadAsync();

        Assert.NotNull(vm.LoadError);
        Assert.Null(vm.Survivor);
    }

    // ---- Chips ----

    [Fact]
    public async Task MarksATagOnEveryCopySharedAndOneOnASingleCopyPartial()
    {
        SetUpClip(1, "original.mp4", tags: [(10, "warframe"), (11, "clutch")]);
        SetUpClip(2, "copy.mp4",     tags: [(10, "warframe")]);

        var vm = Build(1, 2);
        await vm.LoadAsync();

        var shared  = Assert.Single(vm.TagChips, c => c.EntityId == 10);
        var partial = Assert.Single(vm.TagChips, c => c.EntityId == 11);
        Assert.True(shared.IsShared);
        Assert.True(partial.IsPartial);
    }

    [Fact]
    public async Task WarnsAboutWhatWouldBeLostAndCanKeepItAllAtOnce()
    {
        // The bulk-edit rule is that only New chips are written, so a partial one is dropped
        // unless promoted. That is explicit rather than guessed, but easy to miss in bulk.
        SetUpClip(1, "original.mp4", tags: [(11, "clutch")], players: [(20, "Jane")]);
        SetUpClip(2, "copy.mp4");

        var vm = Build(1, 2);
        await vm.LoadAsync();

        Assert.Equal(2, vm.AtRiskCount);
        Assert.True(vm.HasAtRisk);

        vm.KeepEverythingCommand.Execute(null);

        Assert.False(vm.HasAtRisk);
        Assert.All(vm.TagChips, c => Assert.True(c.IsNew));
        Assert.All(vm.PlayerChips, c => Assert.True(c.IsNew));
    }

    [Fact]
    public async Task RecomputesTheChipsWhenTheSurvivorChanges()
    {
        SetUpClip(1, "original.mp4", tags: [(10, "warframe")]);
        SetUpClip(2, "copy.mp4",     tags: [(10, "warframe"), (11, "clutch")]);

        var vm = Build(1, 2);
        await vm.LoadAsync();

        vm.Survivor = vm.Candidates[1];

        Assert.True(vm.Candidates[1].IsSurvivor);
        Assert.False(vm.Candidates[0].IsSurvivor);
        // Statuses describe the group, not the survivor, so they are unchanged - but the list is
        // rebuilt, which is what resets a promotion made against the previous choice.
        Assert.Single(vm.TagChips, c => c.IsPartial);
    }

    // ---- Highlights ----

    [Fact]
    public async Task OffersEveryHighlightFromEveryCopy()
    {
        SetUpClip(1, "original.mp4", highlights: [Highlight(1, 10, 20, "first")]);
        SetUpClip(2, "copy.mp4",     highlights: [Highlight(2, 40, 50, "second")]);

        var vm = Build(1, 2);
        await vm.LoadAsync();

        Assert.Equal(2, vm.Highlights.Count);
        Assert.All(vm.Highlights, h => Assert.True(h.IsSelected));
        Assert.Single(vm.Highlights, h => h.IsOnSurvivor);
    }

    [Fact]
    public async Task CollapsesTheSameRangeAppearingOnBothCopies()
    {
        // Two copies of one recording usually carry the same highlights. Listing both would invite
        // the user to create a duplicate range on the survivor.
        SetUpClip(1, "original.mp4", highlights: [Highlight(1, 10, 20, "same")]);
        SetUpClip(2, "copy.mp4",     highlights: [Highlight(2, 10, 20, "same")]);

        var vm = Build(1, 2);
        await vm.LoadAsync();

        var row = Assert.Single(vm.Highlights);
        Assert.True(row.IsOnSurvivor);
    }

    // ---- Apply ----

    [Fact]
    public void WillNotApplyUntilTheRemovalIsConfirmed()
    {
        var vm = Build(1, 2);

        Assert.False(vm.CanApply);
        vm.RemovalConfirmed = true;
        Assert.False(vm.CanApply);
    }

    [Fact]
    public async Task WritesOnlyPromotedMetadataOntoTheSurvivor()
    {
        SetUpClip(1, "original.mp4", tags: [(10, "warframe")]);
        SetUpClip(2, "copy.mp4",     tags: [(10, "warframe"), (11, "clutch")], players: [(20, "Jane")]);

        var vm = Build(1, 2);
        await vm.LoadAsync();
        vm.RemovalConfirmed = true;

        await vm.ApplyCommand.ExecuteAsync(null);

        // The shared tag is already on the survivor, so nothing is written for it. The partial one
        // was never promoted, so it is not written either.
        _clips.Verify(x => x.AddTagAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _players.Verify(x => x.TagClipAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WritesEverythingKeptOntoTheSurvivor()
    {
        SetUpClip(1, "original.mp4", tags: [(10, "warframe")]);
        SetUpClip(2, "copy.mp4",     tags: [(10, "warframe"), (11, "clutch")], players: [(20, "Jane")]);

        var vm = Build(1, 2);
        await vm.LoadAsync();
        vm.KeepEverythingCommand.Execute(null);
        vm.RemovalConfirmed = true;

        await vm.ApplyCommand.ExecuteAsync(null);

        _clips.Verify(x => x.AddTagAsync(1, 11, It.IsAny<CancellationToken>()), Times.Once);
        _players.Verify(x => x.TagClipAsync(1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecreatesASelectedHighlightFromACopyOnTheSurvivor()
    {
        SetUpClip(1, "original.mp4");
        SetUpClip(2, "copy.mp4", highlights: [Highlight(2, 40, 50, "from the copy")]);

        _highlights.Setup(x => x.CreateAsync(
                       It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(),
                       It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new Highlight { Id = 99 });

        var vm = Build(1, 2);
        await vm.LoadAsync();
        vm.RemovalConfirmed = true;

        await vm.ApplyCommand.ExecuteAsync(null);

        _highlights.Verify(x => x.CreateAsync(
            1, TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(50),
            "from the copy", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletesASurvivorHighlightTheUserUnticked()
    {
        SetUpClip(1, "original.mp4", highlights: [Highlight(1, 10, 20, "unwanted")]);
        SetUpClip(2, "copy.mp4");

        var vm = Build(1, 2);
        await vm.LoadAsync();
        vm.Highlights[0].IsSelected = false;
        vm.RemovalConfirmed = true;

        await vm.ApplyCommand.ExecuteAsync(null);

        _highlights.Verify(x => x.DeleteAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DoesNotRecreateAHighlightTheUserLeftUnticked()
    {
        SetUpClip(1, "original.mp4");
        SetUpClip(2, "copy.mp4", highlights: [Highlight(2, 40, 50)]);

        var vm = Build(1, 2);
        await vm.LoadAsync();
        vm.Highlights[0].IsSelected = false;
        vm.RemovalConfirmed = true;

        await vm.ApplyCommand.ExecuteAsync(null);

        _highlights.Verify(x => x.CreateAsync(
            It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TrashesTheOtherCopiesLastAndLeavesTheSurvivorAlone()
    {
        // Order matters: if removal failed halfway the survivor would still be complete, which is
        // not true the other way round.
        SetUpClip(1, "original.mp4", tags: [(10, "a")]);
        SetUpClip(2, "copy.mp4",     tags: [(11, "b")]);

        var sequence = new List<string>();
        _clips.Setup(x => x.AddTagAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .Callback(() => sequence.Add("tag")).Returns(Task.CompletedTask);
        _clips.Setup(x => x.TrashAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .Callback(() => sequence.Add("trash")).Returns(Task.CompletedTask);

        var vm = Build(1, 2);
        await vm.LoadAsync();
        vm.KeepEverythingCommand.Execute(null);
        vm.RemovalConfirmed = true;

        await vm.ApplyCommand.ExecuteAsync(null);

        _clips.Verify(x => x.TrashAsync(2, It.IsAny<CancellationToken>()), Times.Once);
        _clips.Verify(x => x.TrashAsync(1, It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("trash", sequence[^1]);
        Assert.Contains("tag", sequence);
    }

    [Fact]
    public async Task ForgetsTheGroupAndClosesOnSuccess()
    {
        SetUpClip(1, "original.mp4");
        SetUpClip(2, "copy.mp4");

        var vm = Build(1, 2);
        await vm.LoadAsync();
        vm.RemovalConfirmed = true;

        bool? closedWith = null;
        vm.CloseRequested = applied => closedWith = applied;

        await vm.ApplyCommand.ExecuteAsync(null);

        _finder.Verify(x => x.Forget("hash"), Times.Once);
        Assert.True(closedWith);
    }

    [Fact]
    public async Task ReportsAFailureRatherThanClosingAsIfItWorked()
    {
        SetUpClip(1, "original.mp4");
        SetUpClip(2, "copy.mp4");
        _clips.Setup(x => x.TrashAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("locked"));

        var vm = Build(1, 2);
        await vm.LoadAsync();
        vm.RemovalConfirmed = true;

        var closed = false;
        vm.CloseRequested = _ => closed = true;

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.False(closed);
        Assert.NotNull(vm.LoadError);
        _finder.Verify(x => x.Forget(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void CancelChangesNothing()
    {
        var vm = Build(1, 2);

        bool? closedWith = null;
        vm.CloseRequested = applied => closedWith = applied;

        vm.CancelCommand.Execute(null);

        Assert.False(closedWith);
        _clips.Verify(x => x.TrashAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _clips.Verify(x => x.AddTagAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
