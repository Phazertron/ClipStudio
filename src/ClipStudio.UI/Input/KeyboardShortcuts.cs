using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;

namespace ClipStudio.UI.Input;

/// <summary>
/// Every keyboard shortcut the application has, in one place.
/// </summary>
/// <remarks>
/// This is the single source the key handler and the help list both read. Before it, the clip
/// editor's shortcuts were a <c>switch</c> and nothing else knew they existed, so any list shown
/// to the user would have been a second copy that drifted the first time one was added.
/// <para>
/// The identifiers are stable and separate from the keys, so a future remapping has something to
/// store a preference against without the rest of the application changing.
/// </para>
/// </remarks>
public static class KeyboardShortcuts
{
    // ---- Playback ----

    /// <summary>Starts or pauses playback.</summary>
    public static readonly KeyboardShortcut PlayPause = new(
        "playback.playPause", ShortcutCategory.Playback, "Play or pause", Key.Space);

    /// <summary>Skips backwards.</summary>
    public static readonly KeyboardShortcut SkipBack = new(
        "playback.skipBack", ShortcutCategory.Playback, "Skip back", Key.Left);

    /// <summary>Skips forwards.</summary>
    public static readonly KeyboardShortcut SkipForward = new(
        "playback.skipForward", ShortcutCategory.Playback, "Skip forward", Key.Right);

    /// <summary>Steps back one frame.</summary>
    public static readonly KeyboardShortcut FrameBack = new(
        "playback.frameBack", ShortcutCategory.Playback, "Previous frame", Key.OemComma);

    /// <summary>Steps forward one frame.</summary>
    public static readonly KeyboardShortcut FrameForward = new(
        "playback.frameForward", ShortcutCategory.Playback, "Next frame", Key.OemPeriod);

    /// <summary>Shows or hides subtitles.</summary>
    public static readonly KeyboardShortcut ToggleSubtitles = new(
        "playback.subtitles", ShortcutCategory.Playback, "Show or hide subtitles", Key.C);

    // ---- Navigation ----

    /// <summary>Closes the clip and returns to where it was opened from.</summary>
    public static readonly KeyboardShortcut Back = new(
        "navigation.back", ShortcutCategory.Navigation, "Close the clip", Key.Escape);

    /// <summary>Opens the previous clip in the current sequence.</summary>
    /// <remarks>
    /// Alt, because the bare arrows seek. Seeking is the far more frequent action, so it keeps the
    /// unmodified key.
    /// </remarks>
    public static readonly KeyboardShortcut PreviousClip = new(
        "navigation.previousClip", ShortcutCategory.Navigation, "Previous clip",
        Key.Left, KeyModifiers.Alt);

    /// <summary>Opens the next clip in the current sequence.</summary>
    public static readonly KeyboardShortcut NextClip = new(
        "navigation.nextClip", ShortcutCategory.Navigation, "Next clip",
        Key.Right, KeyModifiers.Alt);

    // ---- Marking ----

    /// <summary>Marks or unmarks the clip as a favourite.</summary>
    public static readonly KeyboardShortcut ToggleFavourite = new(
        "marking.favourite", ShortcutCategory.Marking, "Mark as favourite", Key.F);

    /// <summary>Marks the clip as reviewed.</summary>
    public static readonly KeyboardShortcut MarkReviewed = new(
        "marking.reviewed", ShortcutCategory.Marking, "Mark as reviewed", Key.R);

    /// <summary>Clears the clip's rating.</summary>
    public static readonly KeyboardShortcut RatingClear = new(
        "marking.rating.0", ShortcutCategory.Marking, "Clear the rating", Key.D0);

    /// <summary>Rates the clip one star.</summary>
    public static readonly KeyboardShortcut Rating1 = new(
        "marking.rating.1", ShortcutCategory.Marking, "Rate 1 star", Key.D1);

    /// <summary>Rates the clip two stars.</summary>
    public static readonly KeyboardShortcut Rating2 = new(
        "marking.rating.2", ShortcutCategory.Marking, "Rate 2 stars", Key.D2);

    /// <summary>Rates the clip three stars.</summary>
    public static readonly KeyboardShortcut Rating3 = new(
        "marking.rating.3", ShortcutCategory.Marking, "Rate 3 stars", Key.D3);

    /// <summary>Rates the clip four stars.</summary>
    public static readonly KeyboardShortcut Rating4 = new(
        "marking.rating.4", ShortcutCategory.Marking, "Rate 4 stars", Key.D4);

    /// <summary>Rates the clip five stars.</summary>
    public static readonly KeyboardShortcut Rating5 = new(
        "marking.rating.5", ShortcutCategory.Marking, "Rate 5 stars", Key.D5);

    // ---- Highlights ----

    /// <summary>Jumps to the previous highlight on the clip.</summary>
    public static readonly KeyboardShortcut PreviousHighlight = new(
        "highlights.previous", ShortcutCategory.Highlights, "Previous highlight",
        Key.Up, KeyModifiers.Alt);

    /// <summary>Jumps to the next highlight on the clip.</summary>
    public static readonly KeyboardShortcut NextHighlight = new(
        "highlights.next", ShortcutCategory.Highlights, "Next highlight",
        Key.Down, KeyModifiers.Alt);

    /// <summary>Gets every shortcut, in the order the help list shows them.</summary>
    public static IReadOnlyList<KeyboardShortcut> All { get; } =
    [
        PlayPause, SkipBack, SkipForward, FrameBack, FrameForward, ToggleSubtitles,
        Back, PreviousClip, NextClip,
        ToggleFavourite, MarkReviewed,
        RatingClear, Rating1, Rating2, Rating3, Rating4, Rating5,
        PreviousHighlight, NextHighlight,
    ];

    /// <summary>Gets the rating shortcuts paired with the rating each one sets.</summary>
    /// <remarks>
    /// Kept beside the definitions so the handler does not have to parse a number back out of an
    /// identifier to know what a key means.
    /// </remarks>
    public static IReadOnlyList<(KeyboardShortcut Shortcut, int Rating)> RatingShortcuts { get; } =
    [
        (RatingClear, 0), (Rating1, 1), (Rating2, 2), (Rating3, 3), (Rating4, 4), (Rating5, 5),
    ];

    /// <summary>Returns the shortcuts in one category, in declaration order.</summary>
    /// <param name="category">The category to list.</param>
    /// <returns>The shortcuts in that category.</returns>
    public static IEnumerable<KeyboardShortcut> InCategory(ShortcutCategory category)
        => All.Where(s => s.Category == category);

    /// <summary>Finds the shortcut a key press triggers, if any.</summary>
    /// <param name="key">The key that was pressed.</param>
    /// <param name="modifiers">The modifiers held at the time.</param>
    /// <returns>The matching shortcut, or <see langword="null"/>.</returns>
    public static KeyboardShortcut? Find(Key key, KeyModifiers modifiers)
        => All.FirstOrDefault(s => s.Matches(key, modifiers));
}
