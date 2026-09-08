using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Tests.Fakes;
using ClipStudio.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ClipStudio.Tests.ViewModels;

/// <summary>
/// Covers the staging model behind the library's bulk-edit panel: how chips classify the selection,
/// what Apply actually writes, and the copy-format and destructive paths.
/// </summary>
public class BulkEditViewModelTests
{
    private readonly FakeBulkEditHost _host = new();
    private readonly Mock<IClipService> _clips = new();
    private readonly Mock<IPlayerService> _players = new();
    private readonly Mock<ISettingsService> _settings = new();

    /// <summary>Builds the view model over the fake host and mocked services.</summary>
    private BulkEditViewModel Create()
    {
        _settings.SetupGet(s => s.Current).Returns(new ClipStudio.Application.Models.AppSettings());
        return new BulkEditViewModel(
            _host,
            _clips.Object,
            _players.Object,
            Mock.Of<IServiceScopeFactory>(),
            _settings.Object);
    }

    /// <summary>Builds a clip card carrying the given general tags, players and game tag.</summary>
    private static ClipCardViewModel Card(
        int id,
        IEnumerable<int>? tagIds = null,
        IEnumerable<int>? playerIds = null,
        int? gameTagId = null,
        bool isBroken = false)
    {
        var clip = new Clip { Id = id, FileName = $"clip{id}.mp4", IsBroken = isBroken };

        foreach (var tagId in tagIds ?? [])
            clip.ClipTags.Add(new ClipTag
            {
                TagId = tagId,
                Tag = new Tag { Id = tagId, Name = $"tag{tagId}", Type = TagType.General },
            });

        if (gameTagId is not null)
            clip.ClipTags.Add(new ClipTag
            {
                TagId = gameTagId.Value,
                Tag = new Tag { Id = gameTagId.Value, Name = $"game{gameTagId}", Type = TagType.Game },
            });

        foreach (var playerId in playerIds ?? [])
            clip.ClipPlayers.Add(new ClipPlayer
            {
                PlayerId = playerId,
                Player = new Player { Id = playerId, DisplayName = $"player{playerId}" },
            });

        return new ClipCardViewModel(clip);
    }

    /// <summary>Registers tags, game tags and players so the chip builder can resolve their names.</summary>
    private void Available(int[]? tags = null, int[]? games = null, int[]? players = null)
    {
        _host.TagList     = (tags ?? []).Select(i => new Tag { Id = i, Name = $"tag{i}", Type = TagType.General }).ToList();
        _host.GameTagList = (games ?? []).Select(i => new Tag { Id = i, Name = $"game{i}", Type = TagType.Game }).ToList();
        _host.PlayerList  = (players ?? []).Select(i => new Player { Id = i, DisplayName = $"player{i}" }).ToList();
    }

    // ---- Chip classification ----

    [Fact]
    public void ATagOnEverySelectedClipIsShared()
    {
        Available(tags: [1]);
        _host.SelectionList = [Card(1, tagIds: [1]), Card(2, tagIds: [1])];

        var vm = Create();
        vm.RefreshChipsFromSelection();

        var chip = Assert.Single(vm.BulkPendingTags);
        Assert.Equal(BulkTagStatus.Shared, chip.Status);
    }

    [Fact]
    public void ATagOnOnlySomeSelectedClipsIsPartial()
    {
        Available(tags: [1]);
        _host.SelectionList = [Card(1, tagIds: [1]), Card(2)];

        var vm = Create();
        vm.RefreshChipsFromSelection();

        var chip = Assert.Single(vm.BulkPendingTags);
        Assert.Equal(BulkTagStatus.Partial, chip.Status);
    }

    [Fact]
    public void AStagedTagIsNewEvenWhenNoSelectedClipHasIt()
    {
        Available(tags: [7]);
        _host.SelectionList = [Card(1)];

        var vm = Create();
        vm.BulkSelectedTag = _host.TagList[0];
        vm.AddBulkTagCommand.Execute(null);

        var chip = Assert.Single(vm.BulkPendingTags);
        Assert.Equal(BulkTagStatus.New, chip.Status);
        Assert.Null(vm.BulkSelectedTag);
    }

    [Fact]
    public void PromotingAPartialTagMakesItNew()
    {
        Available(tags: [1]);
        _host.SelectionList = [Card(1, tagIds: [1]), Card(2)];

        var vm = Create();
        vm.RefreshChipsFromSelection();

        var partial = Assert.Single(vm.BulkPendingTags);
        Assert.NotNull(partial.PromoteCommand);
        partial.PromoteCommand!.Execute(null);

        Assert.Equal(BulkTagStatus.New, Assert.Single(vm.BulkPendingTags).Status);
    }

    [Fact]
    public void ASharedTagOffersNoPromotion()
    {
        Available(tags: [1]);
        _host.SelectionList = [Card(1, tagIds: [1]), Card(2, tagIds: [1])];

        var vm = Create();
        vm.RefreshChipsFromSelection();

        Assert.Null(Assert.Single(vm.BulkPendingTags).PromoteCommand);
    }

    [Fact]
    public void AGameOnEverySelectedClipIsSharedAndOnSomeIsPartial()
    {
        Available(games: [9]);

        _host.SelectionList = [Card(1, gameTagId: 9), Card(2, gameTagId: 9)];
        var vm = Create();
        vm.RefreshChipsFromSelection();
        Assert.Equal(BulkTagStatus.Shared, Assert.Single(vm.BulkPendingGames).Status);

        _host.SelectionList = [Card(1, gameTagId: 9), Card(2)];
        vm.RefreshChipsFromSelection();
        Assert.Equal(BulkTagStatus.Partial, Assert.Single(vm.BulkPendingGames).Status);
    }

    [Fact]
    public void PlayersAreClassifiedTheSameWayAsTags()
    {
        Available(players: [4]);
        _host.SelectionList = [Card(1, playerIds: [4]), Card(2)];

        var vm = Create();
        vm.RefreshChipsFromSelection();

        Assert.Equal(BulkTagStatus.Partial, Assert.Single(vm.BulkPendingPlayers).Status);
    }

    [Fact]
    public void ChipsAreEmptyWhenNothingIsSelected()
    {
        Available(tags: [1]);
        _host.SelectionList = [];

        var vm = Create();
        vm.RefreshChipsFromSelection();

        Assert.Empty(vm.BulkPendingTags);
        Assert.Empty(vm.BulkPendingPlayers);
        Assert.Empty(vm.BulkPendingGames);
    }

    // ---- Apply ----

    [Fact]
    public async Task ApplyWritesOnlyNewChipsNotSharedOrPartialOnes()
    {
        Available(tags: [1, 2, 7]);
        _host.SelectionList = [Card(1, tagIds: [1, 2]), Card(2, tagIds: [1])];

        var vm = Create();
        vm.RefreshChipsFromSelection();
        // 1 is Shared, 2 is Partial. Only the explicitly added 7 should be written.
        vm.BulkSelectedTag = _host.TagList.First(t => t.Id == 7);
        vm.AddBulkTagCommand.Execute(null);

        await vm.ApplyBulkEditsCommand.ExecuteAsync(null);

        _clips.Verify(c => c.BulkAddTagAsync(It.IsAny<IEnumerable<int>>(), 7, It.IsAny<CancellationToken>()), Times.Once);
        _clips.Verify(c => c.BulkAddTagAsync(It.IsAny<IEnumerable<int>>(), 1, It.IsAny<CancellationToken>()), Times.Never);
        _clips.Verify(c => c.BulkAddTagAsync(It.IsAny<IEnumerable<int>>(), 2, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyRespectsThePerCategoryToggles()
    {
        Available(tags: [7], players: [4]);
        _host.SelectionList = [Card(1)];

        var vm = Create();
        vm.BulkSelectedTag = _host.TagList[0];
        vm.AddBulkTagCommand.Execute(null);
        vm.BulkSelectedPlayerPicker = _host.PlayerList[0];
        vm.AddBulkPlayerCommand.Execute(null);

        vm.BulkApplyTags = false;

        await vm.ApplyBulkEditsCommand.ExecuteAsync(null);

        _clips.Verify(c => c.BulkAddTagAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _players.Verify(p => p.TagClipAsync(1, 4, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyReloadsAndClearsStaging()
    {
        Available(tags: [7]);
        _host.SelectionList = [Card(1)];

        var vm = Create();
        vm.BulkSelectedTag = _host.TagList[0];
        vm.AddBulkTagCommand.Execute(null);

        await vm.ApplyBulkEditsCommand.ExecuteAsync(null);

        Assert.Equal(1, _host.ReloadCalls);
        Assert.Empty(vm.BulkPendingTags);
    }

    [Fact]
    public async Task ApplyDoesNothingWithAnEmptySelection()
    {
        var vm = Create();

        await vm.ApplyBulkEditsCommand.ExecuteAsync(null);

        Assert.Equal(0, _host.ReloadCalls);
        _clips.Verify(c => c.BulkAddTagAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void CancelDiscardsStagingWithoutWritingAnything()
    {
        Available(tags: [7]);
        _host.SelectionList = [Card(1)];

        var vm = Create();
        vm.BulkSelectedTag = _host.TagList[0];
        vm.AddBulkTagCommand.Execute(null);
        Assert.Single(vm.BulkPendingTags);

        vm.CancelBulkEditsCommand.Execute(null);

        Assert.Empty(vm.BulkPendingTags);
        _clips.Verify(c => c.BulkAddTagAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Removal is immediate ----

    [Fact]
    public void RemovingAnExistingTagChipWritesImmediatelyAndHidesTheChip()
    {
        Available(tags: [1]);
        _host.SelectionList = [Card(1, tagIds: [1]), Card(2, tagIds: [1])];

        var vm = Create();
        vm.RefreshChipsFromSelection();
        var chip = Assert.Single(vm.BulkPendingTags);
        Assert.NotNull(chip.RemoveCommand);
        chip.RemoveCommand.Execute(null);

        Assert.Empty(vm.BulkPendingTags);
        _clips.Verify(c => c.RemoveTagAsync(1, 1, It.IsAny<CancellationToken>()), Times.Once);
        _clips.Verify(c => c.RemoveTagAsync(2, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- Destructive paths ----

    [Fact]
    public async Task RemoveAllBrokenWorksOnTheWholeViewNotTheSelection()
    {
        _host.ClipList      = [Card(1, isBroken: true), Card(2), Card(3, isBroken: true)];
        _host.SelectionList = [_host.ClipList[1]];

        var vm = Create();
        await vm.ConfirmRemoveAllBrokenClipsCommand.ExecuteAsync(null);

        _clips.Verify(c => c.PermanentlyDeleteAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        _clips.Verify(c => c.PermanentlyDeleteAsync(3, It.IsAny<CancellationToken>()), Times.Once);
        _clips.Verify(c => c.PermanentlyDeleteAsync(2, It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(vm.IsRemoveAllBrokenConfirmVisible);
    }

    [Fact]
    public async Task BulkTrashClearsTheSelectionAndReloads()
    {
        _host.SelectionList = [Card(1), Card(2)];

        var vm = Create();
        await vm.BulkTrashCommand.ExecuteAsync(null);

        _clips.Verify(c => c.BulkTrashAsync(It.Is<IEnumerable<int>>(ids => ids.Count() == 2), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, _host.DeselectAllCalls);
        Assert.Equal(1, _host.ReloadCalls);
    }

    [Fact]
    public async Task ClearAllDataResetsEveryAspectOfEachSelectedClip()
    {
        _host.SelectionList = [Card(1)];

        var vm = Create();
        await vm.ConfirmBulkClearAllDataCommand.ExecuteAsync(null);

        _clips.Verify(c => c.ClearTagsAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        _players.Verify(p => p.UntagAllAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        _clips.Verify(c => c.SetRatingAsync(1, 0, It.IsAny<CancellationToken>()), Times.Once);
        _clips.Verify(c => c.SetStatusAsync(1, ClipStatus.Unreviewed, It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(vm.IsBulkClearAllDataConfirmVisible);
    }

    [Fact]
    public async Task ArchiveBrokenOnlyTouchesTheBrokenClipsInTheSelection()
    {
        _host.SelectionList = [Card(1, isBroken: true), Card(2)];

        var vm = Create();
        await vm.BulkArchiveBrokenCommand.ExecuteAsync(null);

        _clips.Verify(c => c.SetStatusAsync(1, ClipStatus.Archived, It.IsAny<CancellationToken>()), Times.Once);
        _clips.Verify(c => c.SetStatusAsync(2, ClipStatus.Archived, It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Copy format ----

    [Fact]
    public void StartingCopyFormatDeselectsTheSourceSoTargetsCanBeChosen()
    {
        var source = Card(1, tagIds: [1]);
        _host.SelectionList = [source];

        var vm = Create();
        vm.StartCopyFormatCommand.Execute(null);

        Assert.True(vm.IsCopyFormatMode);
        Assert.Equal("clip1.mp4", vm.CopyFormatSourceDisplay);
        Assert.True(source.IsCopyFormatSource);
        Assert.Contains((source, false), _host.SelectionChanges);
    }

    [Fact]
    public void CopyFormatNeedsExactlyOneSelectedClip()
    {
        _host.SelectionList = [Card(1), Card(2)];

        var vm = Create();
        vm.StartCopyFormatCommand.Execute(null);

        Assert.False(vm.IsCopyFormatMode);
    }

    [Fact]
    public void ExitingCopyFormatClearsTheSourceMarker()
    {
        var source = Card(1);
        _host.SelectionList = [source];

        var vm = Create();
        vm.StartCopyFormatCommand.Execute(null);
        vm.ExitCopyFormatCommand.Execute(null);

        Assert.False(vm.IsCopyFormatMode);
        Assert.False(source.IsCopyFormatSource);
        Assert.Equal(string.Empty, vm.CopyFormatSourceDisplay);
    }

    [Fact]
    public async Task PastingFormatAppliesTheSourceAspectsThatAreToggledOn()
    {
        var source = Card(1, tagIds: [5], playerIds: [4], gameTagId: 9);
        _host.SelectionList = [source];

        var vm = Create();
        vm.StartCopyFormatCommand.Execute(null);

        var target = Card(2);
        _host.SelectionList = [target];

        vm.BulkCopyPastePlayers = false;
        await vm.PasteFormatToSelectionCommand.ExecuteAsync(null);

        _clips.Verify(c => c.BulkAddTagAsync(It.IsAny<IEnumerable<int>>(), 5, It.IsAny<CancellationToken>()), Times.Once);
        _clips.Verify(c => c.BulkSetGameAsync(It.IsAny<IEnumerable<int>>(), 9, It.IsAny<CancellationToken>()), Times.Once);
        _players.Verify(p => p.TagClipAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(vm.IsCopyFormatMode);
    }

    [Fact]
    public async Task PastingWithoutASourceDoesNothing()
    {
        _host.SelectionList = [Card(1)];

        var vm = Create();
        await vm.PasteFormatToSelectionCommand.ExecuteAsync(null);

        Assert.Equal(0, _host.ReloadCalls);
    }

    // ---- Selection notifications ----

    [Fact]
    public void TheFirstSelectionOpensThePanel()
    {
        var vm = Create();
        Assert.False(vm.IsPanelOpen);

        _host.SelectionList = [Card(1)];
        vm.NotifySelectionChanged();

        Assert.True(vm.IsPanelOpen);
    }

    [Fact]
    public void APanelClosedByHandStaysClosedUntilTheSelectionGrowsAgain()
    {
        var vm = Create();
        _host.SelectionList = [Card(1)];
        vm.NotifySelectionChanged();

        vm.TogglePanelCommand.Execute(null);
        Assert.False(vm.IsPanelOpen);

        // A further change while clips are still selected re-opens it, which is the documented
        // auto-open behaviour rather than an oversight.
        vm.NotifySelectionChanged();
        Assert.True(vm.IsPanelOpen);
    }

    [Fact]
    public void TranscriptionIsOfferedOnlyWhileClipsAreSelected()
    {
        var vm = Create();
        Assert.False(vm.CanBulkTranscribe);

        _host.SelectionList = [Card(1)];
        vm.NotifySelectionChanged();

        Assert.True(vm.CanBulkTranscribe);
    }

    [Fact]
    public void ClosingConfirmationsHidesEveryStripAndThePanel()
    {
        var vm = Create();
        vm.ShowBulkDeleteConfirmCommand.Execute(null);
        vm.ShowRemoveAllTagsConfirmCommand.Execute(null);
        vm.ShowRemoveAllPlayersConfirmCommand.Execute(null);
        vm.ShowRemoveAllGameConfirmCommand.Execute(null);
        vm.ShowBulkClearAllDataConfirmCommand.Execute(null);

        vm.CloseConfirmations();

        Assert.False(vm.IsPanelOpen);
        Assert.False(vm.IsBulkDeleteConfirmVisible);
        Assert.False(vm.IsRemoveAllTagsConfirmVisible);
        Assert.False(vm.IsRemoveAllPlayersConfirmVisible);
        Assert.False(vm.IsRemoveAllGameConfirmVisible);
        Assert.False(vm.IsBulkClearAllDataConfirmVisible);
    }

    [Fact]
    public void CopyFormatButtonNeedsExactlyOneSelectionAndNotAlreadyCopying()
    {
        var vm = Create();
        Assert.False(vm.IsCopyFormatButtonEnabled);

        _host.SelectionList = [Card(1)];
        vm.NotifySelectionChanged();
        Assert.True(vm.IsCopyFormatButtonEnabled);

        vm.StartCopyFormatCommand.Execute(null);
        Assert.False(vm.IsCopyFormatButtonEnabled);
    }
}
