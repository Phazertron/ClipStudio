using Avalonia.Input;
using ClipStudio.Tests.Fakes;
using ClipStudio.UI.Input;
using ClipStudio.UI.ViewModels.Settings;

namespace ClipStudio.Tests.Input;

/// <summary>
/// Unit tests for the keyboard shortcut registry.
/// </summary>
/// <remarks>
/// The registry exists so the key handler and the help list read one source. These tests guard the
/// properties that make that worth having: no two shortcuts claim the same gesture, and the list
/// shown to the user is the list the handler acts on.
/// </remarks>
public sealed class KeyboardShortcutsTests
{
    [Fact]
    public void NoTwoShortcutsClaimTheSameGesture()
    {
        // The first duplicate would silently shadow the second, since lookup takes the first match.
        var clashes = KeyboardShortcuts.All
            .GroupBy(s => (s.Key, s.Modifiers))
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.First().GestureDisplay}: {string.Join(", ", g.Select(s => s.Id))}")
            .ToList();

        Assert.Empty(clashes);
    }

    [Fact]
    public void EveryShortcutHasAStableUniqueIdentifier()
    {
        // Identifiers are what a stored remapping would key on, so a duplicate would make one
        // shortcut impossible to address.
        var ids = KeyboardShortcuts.All.Select(s => s.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
    }

    [Fact]
    public void EveryShortcutDescribesItself()
    {
        Assert.All(KeyboardShortcuts.All,
            s => Assert.False(string.IsNullOrWhiteSpace(s.Description)));
    }

    [Theory]
    [InlineData(Key.Space, KeyModifiers.None, "playback.playPause")]
    [InlineData(Key.Left, KeyModifiers.None, "playback.skipBack")]
    [InlineData(Key.Left, KeyModifiers.Alt, "navigation.previousClip")]
    [InlineData(Key.Escape, KeyModifiers.None, "navigation.back")]
    [InlineData(Key.D3, KeyModifiers.None, "marking.rating.3")]
    public void FindsTheShortcutForAPress(Key key, KeyModifiers modifiers, string expectedId)
    {
        Assert.Equal(expectedId, KeyboardShortcuts.Find(key, modifiers)?.Id);
    }

    [Fact]
    public void AModifiedPressDoesNotTriggerTheUnmodifiedShortcut()
    {
        // Alt+Left is "previous clip" and plain Left is "skip back". Matching loosely would make
        // the arrows do both, which is exactly why the modifier is on the rarer action.
        var plain = KeyboardShortcuts.Find(Key.Left, KeyModifiers.None);
        var alt   = KeyboardShortcuts.Find(Key.Left, KeyModifiers.Alt);

        Assert.NotEqual(plain?.Id, alt?.Id);
    }

    [Fact]
    public void AnUnboundPressFindsNothing()
    {
        Assert.Null(KeyboardShortcuts.Find(Key.Q, KeyModifiers.None));
        Assert.Null(KeyboardShortcuts.Find(Key.Space, KeyModifiers.Control));
    }

    [Theory]
    [InlineData(Key.Space, KeyModifiers.None, "Space")]
    [InlineData(Key.OemComma, KeyModifiers.None, ",")]
    [InlineData(Key.Left, KeyModifiers.Alt, "Alt + Left arrow")]
    [InlineData(Key.D5, KeyModifiers.None, "5")]
    public void RendersAGestureTheWayAKeyboardPrintsIt(Key key, KeyModifiers modifiers, string expected)
    {
        // "OemComma" and "D5" are how the enum spells them, not how the key is labelled.
        var shortcut = new KeyboardShortcut("test", ShortcutCategory.Playback, "test", key, modifiers);

        Assert.Equal(expected, shortcut.GestureDisplay);
    }

    [Fact]
    public void EveryRatingShortcutMapsToItsNumber()
    {
        Assert.Equal(6, KeyboardShortcuts.RatingShortcuts.Count);

        foreach (var (shortcut, rating) in KeyboardShortcuts.RatingShortcuts)
        {
            Assert.EndsWith(rating.ToString(), shortcut.Id);
            Assert.Contains(shortcut, KeyboardShortcuts.All);
        }
    }

    // ---- The help list ----

    [Fact]
    public void TheHelpListShowsEveryShortcutAndInventsNone()
    {
        // The whole point of the registry: a hand-maintained list beside the handler would drift
        // the first time someone added a shortcut and forgot the other half.
        var listed = ShortcutGroupViewModel.BuildAll()
            .SelectMany(g => g.Shortcuts)
            .ToList();

        Assert.Equal(KeyboardShortcuts.All.Count, listed.Count);
        Assert.All(KeyboardShortcuts.All, s => Assert.Contains(s, listed));
    }

    [Fact]
    public void TheHelpListGroupsAndTitlesEveryCategoryItShows()
    {
        var groups = ShortcutGroupViewModel.BuildAll();

        Assert.NotEmpty(groups);
        Assert.All(groups, g => Assert.False(string.IsNullOrWhiteSpace(g.Title)));
        Assert.All(groups, g => Assert.NotEmpty(g.Shortcuts));
    }

    [Fact]
    public void TheSectionNamesItselfAndCarriesTheGroups()
    {
        var section = new ShortcutsSectionViewModel(new FakeSettingsSectionHost());

        Assert.Equal("Keyboard shortcuts", section.Title);
        Assert.NotEmpty(section.Groups);
    }
}
