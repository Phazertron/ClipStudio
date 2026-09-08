using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Tests.Fakes;
using ClipStudio.UI.ViewModels;
using Moq;
using Xunit;

namespace ClipStudio.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="HighlightEditorViewModel"/> covering the add form, the inline edit of
/// an existing highlight, and how the shared timeline handles route between the two.
/// </summary>
public sealed class HighlightEditorViewModelTests
{
    private readonly FakeHighlightEditorHost _host = new();
    private readonly Mock<IHighlightService> _highlights = new();

    /// <summary>Sets up a host with a two-minute clip open.</summary>
    public HighlightEditorViewModelTests()
    {
        _host.CurrentClip = new Clip
        {
            Id = 1,
            FilePath = "/clips/Replay.mp4",
            FileName = "Replay.mp4",
            Duration = TimeSpan.FromMinutes(2)
        };
        _host.DurationSeconds = 120;

        _highlights
            .Setup(s => s.CreateAsync(
                It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Highlight { Id = 99, ClipId = 1 });
    }

    /// <summary>Builds the editor under test.</summary>
    /// <returns>An editor wired to the fakes.</returns>
    private HighlightEditorViewModel Create() => new(_host, _highlights.Object);

    /// <summary>Builds an editor with the add form already open.</summary>
    /// <returns>An editor in add mode.</returns>
    private HighlightEditorViewModel CreateAdding()
    {
        var vm = Create();
        vm.BeginAddHighlightCommand.Execute(null);
        return vm;
    }

    /// <summary>Builds a highlight row the editor can be pointed at.</summary>
    /// <param name="startSeconds">The row's range start.</param>
    /// <param name="endSeconds">The row's range end.</param>
    /// <returns>A highlight view model.</returns>
    private static HighlightViewModel Row(double startSeconds, double endSeconds)
        => new(
            new Highlight
            {
                Id = 5,
                Label = "Existing",
                StartTime = TimeSpan.FromSeconds(startSeconds),
                EndTime = TimeSpan.FromSeconds(endSeconds)
            },
            TimeSpan.FromMinutes(2),
            _ => { },
            _ => Task.CompletedTask,
            [],
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask,
            (_, _, _, _) => Task.CompletedTask);

    // ---- The add form ----

    [Fact]
    public void BeginAdd_OpensTheFormAndClosesTheTrimForm()
    {
        var vm = CreateAdding();

        Assert.True(vm.IsAddingHighlight);
        Assert.True(vm.IsAddingOrEditingHighlight);
        Assert.Equal(1, _host.PrepareCalls);
    }

    [Fact]
    public void OpeningAndClosing_ReportsTheEditingStateChange()
    {
        var vm = CreateAdding();
        var afterOpen = _host.EditingStateChangedCalls;

        vm.CancelAddHighlightCommand.Execute(null);

        Assert.Equal(afterOpen + 1, _host.EditingStateChangedCalls);
        Assert.False(vm.IsAddingOrEditingHighlight);
    }

    [Fact]
    public void Cancel_ClearsEverythingTheFormHeld()
    {
        var vm = CreateAdding();
        vm.NewHighlightLabel = "Ace";
        vm.HighlightStartDisplay = "0:10";
        vm.HighlightEndDisplay = "0:20";
        vm.AddPendingHighlightTag(new Tag { Id = 3, Name = "Clutch", Type = TagType.General });

        vm.CancelAddHighlightCommand.Execute(null);

        Assert.False(vm.IsAddingHighlight);
        Assert.Equal(string.Empty, vm.NewHighlightLabel);
        Assert.Equal("0:00", vm.HighlightStartDisplay);
        Assert.Equal("0:00", vm.HighlightEndDisplay);
        Assert.Empty(vm.PendingHighlightTags);
        Assert.Equal(0, vm.HighlightStartFraction);
    }

    [Fact]
    public void TypingARange_MovesTheHandles()
    {
        var vm = CreateAdding();

        vm.HighlightStartDisplay = "0:30";
        vm.HighlightEndDisplay = "1:30";

        Assert.Equal(0.25, vm.HighlightStartFraction, 6);
        Assert.Equal(0.75, vm.HighlightEndFraction, 6);
    }

    [Fact]
    public void TypingARange_IsIgnoredWhenTheFormIsClosed()
    {
        var vm = Create();

        vm.HighlightStartDisplay = "0:30";

        Assert.Equal(0, vm.HighlightStartFraction);
    }

    [Fact]
    public void MarkStartAndEnd_UseThePlayHead()
    {
        var vm = CreateAdding();

        _host.CurrentPositionSeconds = 30;
        vm.MarkHighlightStartCommand.Execute(null);

        _host.CurrentPositionSeconds = 90;
        vm.MarkHighlightEndCommand.Execute(null);

        Assert.Equal("0:30.0", vm.HighlightStartDisplay);
        Assert.Equal("1:30.0", vm.HighlightEndDisplay);
        Assert.Equal(0.25, vm.HighlightStartFraction, 6);
        Assert.Equal(0.75, vm.HighlightEndFraction, 6);
    }

    [Fact]
    public void DraggingAHandle_MovesTheAddFormRangeAndClampsToTheClip()
    {
        var vm = CreateAdding();

        vm.SetHighlightStartFromFraction(0.5);
        Assert.Equal("1:00.0", vm.HighlightStartDisplay);

        vm.SetHighlightEndFromFraction(2.0);
        Assert.Equal(1, vm.HighlightEndFraction, 6);

        vm.SetHighlightStartFromFraction(-1);
        Assert.Equal(0, vm.HighlightStartFraction, 6);
    }

    [Fact]
    public void PendingTags_AreAddedOnceAndCanBeRemoved()
    {
        var vm = CreateAdding();
        var tag = new Tag { Id = 3, Name = "Clutch", Type = TagType.General };

        vm.AddPendingHighlightTag(tag);
        vm.AddPendingHighlightTag(tag);

        Assert.Single(vm.PendingHighlightTags);

        vm.PendingHighlightTags[0].RemoveCommand.Execute(null);
        Assert.Empty(vm.PendingHighlightTags);
    }

    // ---- Saving ----

    [Fact]
    public async Task Save_WithoutAClip_DoesNothing()
    {
        _host.CurrentClip = null;
        var vm = CreateAdding();

        await vm.SaveHighlightCommand.ExecuteAsync(null);

        _highlights.Verify(s => s.CreateAsync(
            It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Save_WithAnInvertedRange_ReportsItAndDoesNotCreate()
    {
        var vm = CreateAdding();
        vm.SetHighlightStartFromFraction(0.75);
        vm.SetHighlightEndFromFraction(0.25);

        await vm.SaveHighlightCommand.ExecuteAsync(null);

        Assert.Equal("End time must be after start time.", vm.HighlightAddError);
        Assert.True(vm.IsAddingHighlight);
        _highlights.Verify(s => s.CreateAsync(
            It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Save_CreatesTheHighlightAndClosesTheForm()
    {
        var vm = CreateAdding();
        vm.NewHighlightLabel = "  Ace  ";
        vm.SetHighlightStartFromFraction(0.25);
        vm.SetHighlightEndFromFraction(0.75);

        await vm.SaveHighlightCommand.ExecuteAsync(null);

        _highlights.Verify(s => s.CreateAsync(
            1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(90), "Ace",
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);

        Assert.False(vm.IsAddingHighlight);
        Assert.Equal(1, _host.HighlightCreatedCalls);
    }

    [Fact]
    public async Task Save_WithABlankLabel_PassesNull()
    {
        var vm = CreateAdding();
        vm.NewHighlightLabel = "   ";
        vm.SetHighlightStartFromFraction(0.25);
        vm.SetHighlightEndFromFraction(0.75);

        await vm.SaveHighlightCommand.ExecuteAsync(null);

        _highlights.Verify(s => s.CreateAsync(
            1, It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), null,
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Save_AppliesThePendingTagsToTheNewHighlight()
    {
        var vm = CreateAdding();
        vm.SetHighlightStartFromFraction(0.25);
        vm.SetHighlightEndFromFraction(0.75);
        vm.AddPendingHighlightTag(new Tag { Id = 3, Name = "Clutch", Type = TagType.General });
        vm.AddPendingHighlightTag(new Tag { Id = 4, Name = "Ace", Type = TagType.General });

        await vm.SaveHighlightCommand.ExecuteAsync(null);

        _highlights.Verify(s => s.AddTagAsync(99, 3, It.IsAny<CancellationToken>()), Times.Once);
        _highlights.Verify(s => s.AddTagAsync(99, 4, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- Inline edit of an existing row ----

    [Fact]
    public void EditingARow_TakesOverTheHandles()
    {
        var vm = Create();
        var row = Row(30, 90);

        vm.OnHighlightEditingChanged(row, isEditing: true);

        Assert.Same(row, vm.EditingHighlight);
        Assert.True(vm.IsAddingOrEditingHighlight);
        Assert.Equal(0.25, vm.HighlightStartFraction, 6);
        Assert.Equal(0.75, vm.HighlightEndFraction, 6);
    }

    [Fact]
    public void EditingARow_RoutesMarksToThatRowNotTheAddForm()
    {
        var vm = Create();
        var row = Row(30, 90);
        vm.OnHighlightEditingChanged(row, isEditing: true);

        _host.CurrentPositionSeconds = 60;
        vm.MarkHighlightStartCommand.Execute(null);

        Assert.Equal("1:00.0", row.EditStartDisplay);
        Assert.Equal("0:00", vm.HighlightStartDisplay); // the add form is untouched
        Assert.Equal(0.5, vm.HighlightStartFraction, 6);
    }

    [Fact]
    public void EditingARow_TypingIntoItMovesTheHandles()
    {
        var vm = Create();
        var row = Row(30, 90);
        vm.OnHighlightEditingChanged(row, isEditing: true);

        row.EditStartDisplay = "0:12";
        row.EditEndDisplay = "0:24";

        Assert.Equal(0.1, vm.HighlightStartFraction, 6);
        Assert.Equal(0.2, vm.HighlightEndFraction, 6);
    }

    [Fact]
    public void LeavingEdit_HandsTheHandlesBackToTheAddForm()
    {
        var vm = Create();
        var row = Row(30, 90);
        vm.OnHighlightEditingChanged(row, isEditing: true);

        vm.OnHighlightEditingChanged(row, isEditing: false);

        Assert.Null(vm.EditingHighlight);
        Assert.False(vm.IsAddingOrEditingHighlight);
        Assert.Equal(0, vm.HighlightStartFraction, 6);
    }

    [Fact]
    public void LeavingEdit_ForADifferentRow_DoesNotClearTheCurrentOne()
    {
        var vm = Create();
        var edited = Row(30, 90);
        var other = Row(0, 10);
        vm.OnHighlightEditingChanged(edited, isEditing: true);

        vm.OnHighlightEditingChanged(other, isEditing: false);

        Assert.Same(edited, vm.EditingHighlight);
    }

    [Fact]
    public void StopEditing_ReportsWhetherThereWasAnythingToStop()
    {
        var vm = Create();
        Assert.False(vm.StopEditing());

        vm.OnHighlightEditingChanged(Row(30, 90), isEditing: true);
        Assert.True(vm.StopEditing());
        Assert.Null(vm.EditingHighlight);
    }

    [Fact]
    public void StopEditing_DetachesFromTheRow()
    {
        var vm = Create();
        var row = Row(30, 90);
        vm.OnHighlightEditingChanged(row, isEditing: true);
        vm.StopEditing();

        // The row is about to be replaced; its edits must no longer move the handles.
        row.EditStartDisplay = "1:00";

        Assert.Equal(0, vm.HighlightStartFraction, 6);
    }

    [Fact]
    public void FractionsAreZero_WhenTheDurationIsNotKnownYet()
    {
        _host.DurationSeconds = 0;
        var vm = CreateAdding();
        vm.HighlightStartDisplay = "0:30";

        Assert.Equal(0, vm.HighlightStartFraction);
        Assert.Equal(0, vm.HighlightEndFraction);
    }
}
