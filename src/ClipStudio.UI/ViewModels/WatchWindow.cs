using System;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// A highlight's time range, constrained to what the media can actually play.
/// </summary>
/// <remarks>
/// <para>
/// Nothing guarantees a highlight lies inside its clip. A clip can be relocated to a shorter file,
/// trimmed destructively, or seeded with ranges that were never checked, and the range is stored
/// independently of the clip's duration.
/// </para>
/// <para>
/// An unchecked range is not merely cosmetic. Watch mode seeks to the range start and treats the
/// player's <c>EndReached</c> as "the highlight finished", restarting the media to park the play
/// head back at the start. If the start is past the end of the media, that seek lands at the end,
/// <c>EndReached</c> fires again immediately, and the restart repeats without ever making progress -
/// the app locks up. Clamping the range before it reaches the player is what stops that.
/// </para>
/// </remarks>
/// <param name="Start">The clamped start, never before the media start.</param>
/// <param name="End">The clamped end, never past the media end.</param>
/// <param name="IsUsable">
/// Whether enough of the range survived clamping to be worth playing. False means the highlight
/// falls outside the media entirely and cannot be watched.
/// </param>
public readonly record struct WatchWindow(TimeSpan Start, TimeSpan End, bool IsUsable)
{
    /// <summary>
    /// The shortest window still treated as playable. A window below this is indistinguishable from
    /// an instant seek to the end, which is the case that spins.
    /// </summary>
    public static readonly TimeSpan MinimumUsableLength = TimeSpan.FromMilliseconds(100);

    /// <summary>Gets the length of the clamped window.</summary>
    public TimeSpan Length => End - Start;

    /// <summary>
    /// Constrains a highlight range to the media that has to play it.
    /// </summary>
    /// <param name="start">The highlight's stored start.</param>
    /// <param name="end">The highlight's stored end.</param>
    /// <param name="mediaDuration">
    /// The clip's duration. When this is zero or negative the duration is not known yet, so the
    /// range is passed through unclamped rather than being clamped to nothing.
    /// </param>
    /// <returns>The clamped window, and whether it is still playable.</returns>
    public static WatchWindow Clamp(TimeSpan start, TimeSpan end, TimeSpan mediaDuration)
    {
        if (mediaDuration <= TimeSpan.Zero)
            return new WatchWindow(start, end, end - start >= MinimumUsableLength);

        var clampedEnd   = end   < mediaDuration ? end   : mediaDuration;
        var clampedStart = start < TimeSpan.Zero ? TimeSpan.Zero : start;

        // A start past the (already clamped) end would invert the window; pin it to the end so the
        // window is empty rather than negative, and let IsUsable reject it.
        if (clampedStart > clampedEnd)
            clampedStart = clampedEnd;

        return new WatchWindow(
            clampedStart,
            clampedEnd,
            clampedEnd - clampedStart >= MinimumUsableLength);
    }
}
