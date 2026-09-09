using Avalonia.Input;
using ClipStudio.Core.Entities;
using ClipStudio.Tests.Fakes;
using ClipStudio.UI.Behaviors;

namespace ClipStudio.Tests.Behaviors;

/// <summary>
/// Covers the three-phase commit state machine shared by every tag picker. The cases mirror the
/// paths a user can take through an AutoCompleteBox: clicking a suggestion, arrowing to one and
/// pressing Enter, typing a name and pressing Enter or Tab, and backing out with Escape.
/// </summary>
public class TagPickerStateMachineTests
{
    /// <summary>Builds a tag for use as a suggestion.</summary>
    private static Tag MakeTag(int id = 1, string name = "Ace") => new() { Id = id, Name = name };

    /// <summary>Creates a machine over a fresh fake host.</summary>
    private static (TagPickerStateMachine Machine, FakeTagPickerHost Host) Create(bool refocus = false)
    {
        var host = new FakeTagPickerHost();
        return (new TagPickerStateMachine(host, refocus), host);
    }

    [Fact]
    public void ClickingASuggestionCommitsItOnClose()
    {
        var (machine, host) = Create();
        var tag = MakeTag();

        host.SelectedTag = tag;
        host.Text        = "Ac";
        machine.NotifySelectionChanged();
        machine.NotifyDropDownClosed();

        Assert.Equal(new[] { tag }, host.Committed);
        Assert.Null(host.SelectedTag);
        Assert.Equal(string.Empty, host.Text);
    }

    [Fact]
    public void ArrowNavigationAloneDoesNotCommit()
    {
        var (machine, host) = Create();

        machine.NotifyKeyDown(Key.Down);
        host.SelectedTag = MakeTag();
        machine.NotifySelectionChanged();
        machine.NotifyDropDownClosed();

        Assert.Empty(host.Committed);
    }

    [Fact]
    public void ArrowNavigationFollowedByEnterCommits()
    {
        var (machine, host) = Create();
        var tag = MakeTag();

        machine.NotifyKeyDown(Key.Down);
        host.SelectedTag = tag;
        machine.NotifySelectionChanged();
        machine.NotifyKeyDown(Key.Enter);
        machine.NotifyDropDownClosed();

        Assert.Equal(new[] { tag }, host.Committed);
    }

    [Fact]
    public void ArrowNavigationFlagIsConsumedBySingleSelectionChange()
    {
        var (machine, host) = Create();
        var second = MakeTag(2, "Bravo");

        // Arrow down, then a click on a different row: only the arrow move counts as navigation.
        machine.NotifyKeyDown(Key.Down);
        host.SelectedTag = MakeTag();
        machine.NotifySelectionChanged();

        host.SelectedTag = second;
        machine.NotifySelectionChanged();
        machine.NotifyDropDownClosed();

        Assert.Equal(new[] { second }, host.Committed);
    }

    [Fact]
    public void SelectionClearedByAvaloniaKeepsThePendingTag()
    {
        var (machine, host) = Create();
        var tag = MakeTag();

        host.SelectedTag = tag;
        machine.NotifySelectionChanged();

        // Avalonia clears SelectedItem before raising DropDownClosed.
        host.SelectedTag = null;
        machine.NotifySelectionChanged();
        machine.NotifyDropDownClosed();

        Assert.Equal(new[] { tag }, host.Committed);
    }

    [Fact]
    public void EscapeDiscardsThePendingTag()
    {
        var (machine, host) = Create();

        host.SelectedTag = MakeTag();
        machine.NotifySelectionChanged();
        machine.NotifyKeyDown(Key.Escape);
        machine.NotifyDropDownClosed();

        Assert.Empty(host.Committed);
        Assert.Null(host.SelectedTag);
    }

    [Fact]
    public void EscapeOnlyAffectsTheCloseThatFollowsIt()
    {
        var (machine, host) = Create();
        var tag = MakeTag();

        machine.NotifyKeyDown(Key.Escape);
        machine.NotifyDropDownClosed();

        host.SelectedTag = tag;
        machine.NotifySelectionChanged();
        machine.NotifyDropDownClosed();

        Assert.Equal(new[] { tag }, host.Committed);
    }

    [Fact]
    public void EnterOnTypedTextResolvesAndCommitsWithoutASelection()
    {
        var (machine, host) = Create();
        var tag = MakeTag();

        host.Text           = "Ace";
        host.TextResolution = tag;
        machine.NotifyKeyDown(Key.Enter);

        Assert.Equal(new[] { tag }, host.Committed);
        Assert.Equal(string.Empty, host.Text);
    }

    [Fact]
    public void EnterOnTypedTextDoesNotCommitTwiceWhenTheDropDownCloses()
    {
        var (machine, host) = Create();

        host.Text           = "Ace";
        host.TextResolution = MakeTag();
        machine.NotifyKeyDown(Key.Enter);
        machine.NotifyDropDownClosed();

        Assert.Single(host.Committed);
    }

    [Fact]
    public void EnterOnUnresolvableTextCommitsNothing()
    {
        var (machine, host) = Create();

        host.Text = "nothing matches this";
        machine.NotifyKeyDown(Key.Enter);
        machine.NotifyDropDownClosed();

        Assert.Empty(host.Committed);
    }

    [Fact]
    public void TabCommitsImmediatelyAndOnlyOnce()
    {
        var (machine, host) = Create();
        var tag = MakeTag();

        host.Text           = "Ace";
        host.TextResolution = tag;
        machine.NotifyKeyDown(Key.Tab);

        Assert.Equal(new[] { tag }, host.Committed);

        // Clearing the field closes the dropdown; that close must not commit a second time.
        machine.NotifyDropDownClosed();
        Assert.Single(host.Committed);
    }

    [Fact]
    public void UnresolvableTabLeavesTheCloseFreeToCommitThePendingTag()
    {
        var (machine, host) = Create();
        var tag = MakeTag();

        host.SelectedTag = tag;
        machine.NotifySelectionChanged();

        // Nothing to resolve once the selection is cleared, so Tab must not set the guard flag
        // that would make the close skip the pending commit.
        host.SelectedTag = null;
        machine.NotifyKeyDown(Key.Tab);
        machine.NotifyDropDownClosed();

        Assert.Equal(new[] { tag }, host.Committed);
    }

    [Fact]
    public void FocusIsNotTakenBackWhenRefocusIsOff()
    {
        var (machine, host) = Create(refocus: false);

        host.SelectedTag = MakeTag();
        machine.NotifySelectionChanged();
        machine.NotifyDropDownClosed();

        Assert.Equal(0, host.FocusCalls);
    }

    [Fact]
    public void FocusReturnsToThePickerAfterACommitWhenRefocusIsOn()
    {
        var (machine, host) = Create(refocus: true);

        host.SelectedTag = MakeTag();
        machine.NotifySelectionChanged();
        machine.NotifyDropDownClosed();

        Assert.Equal(1, host.FocusCalls);
    }

    [Fact]
    public void FocusReturnsToThePickerAfterEscapeWhenRefocusIsOn()
    {
        var (machine, host) = Create(refocus: true);

        machine.NotifyKeyDown(Key.Escape);
        machine.NotifyDropDownClosed();

        Assert.Equal(1, host.FocusCalls);
    }

    [Fact]
    public void EnterOnTypedTextReturnsFocusWhenRefocusIsOn()
    {
        var (machine, host) = Create(refocus: true);

        host.Text           = "Ace";
        host.TextResolution = MakeTag();
        machine.NotifyKeyDown(Key.Enter);

        Assert.Equal(1, host.FocusCalls);
    }

    // ---- Tab: take the tag and stay, or move on ----

    [Fact]
    public void TabWithAMatchIsConsumedSoFocusStaysInThePicker()
    {
        var (machine, host) = Create();

        host.Text           = "Ace";
        host.TextResolution = MakeTag();

        var handled = machine.NotifyKeyDown(Key.Tab);

        // Handled keeps Tab from moving focus, so the user can type the next tag straight away.
        Assert.True(handled);
        Assert.Single(host.Committed);
        Assert.Equal(1, host.FocusCalls);
        Assert.Equal(string.Empty, host.Text);
    }

    [Fact]
    public void TabOnAnEmptyPickerIsNotConsumedSoFocusMovesOn()
    {
        var (machine, host) = Create();

        var handled = machine.NotifyKeyDown(Key.Tab);

        Assert.False(handled);
        Assert.Empty(host.Committed);
        Assert.Equal(0, host.FocusCalls);
    }

    [Fact]
    public void TabOnTextMatchingNoTagIsNotConsumed()
    {
        var (machine, host) = Create();

        host.Text = "not a tag";

        var handled = machine.NotifyKeyDown(Key.Tab);

        Assert.False(handled);
        Assert.Empty(host.Committed);
    }

    [Fact]
    public void TabTwiceTakesTheTagThenLeavesThePicker()
    {
        var (machine, host) = Create();
        var tag = MakeTag();

        // First Tab: a tag is showing, so it is taken and the picker keeps focus.
        host.Text           = "Ace";
        host.TextResolution = tag;
        Assert.True(machine.NotifyKeyDown(Key.Tab));

        // The commit cleared the field, so there is nothing left to take.
        host.TextResolution = null;
        Assert.False(machine.NotifyKeyDown(Key.Tab));

        Assert.Equal(new[] { tag }, host.Committed);
    }

    [Fact]
    public void OnlyTabIsEverConsumed()
    {
        var (machine, host) = Create();
        var tag = MakeTag();

        host.SelectedTag = tag;
        machine.NotifySelectionChanged();

        Assert.False(machine.NotifyKeyDown(Key.Enter));
        Assert.False(machine.NotifyKeyDown(Key.Escape));
        Assert.False(machine.NotifyKeyDown(Key.Down));
        Assert.False(machine.NotifyKeyDown(Key.Up));
        Assert.False(machine.NotifyKeyDown(Key.A));
    }

    [Fact]
    public void UnrelatedKeysAreIgnored()
    {
        var (machine, host) = Create();
        var tag = MakeTag();

        host.SelectedTag = tag;
        machine.NotifySelectionChanged();
        machine.NotifyKeyDown(Key.A);
        machine.NotifyDropDownClosed();

        Assert.Equal(new[] { tag }, host.Committed);
    }

    [Fact]
    public void ConsecutiveCommitsEachStartFromCleanState()
    {
        var (machine, host) = Create();
        var first  = MakeTag();
        var second = MakeTag(2, "Bravo");

        host.SelectedTag = first;
        machine.NotifySelectionChanged();
        machine.NotifyDropDownClosed();

        host.SelectedTag = second;
        machine.NotifySelectionChanged();
        machine.NotifyDropDownClosed();

        Assert.Equal(new[] { first, second }, host.Committed);
    }
}
