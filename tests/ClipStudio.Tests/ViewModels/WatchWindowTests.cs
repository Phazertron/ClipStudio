using ClipStudio.UI.ViewModels;

namespace ClipStudio.Tests.ViewModels;

/// <summary>
/// Covers clamping a highlight's range to the media that has to play it. The case that matters is
/// a range starting past the end of the clip: unclamped, watch mode restarts the player forever and
/// the app locks up.
/// </summary>
public class WatchWindowTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void ARangeInsideTheClipIsLeftAlone()
    {
        var w = WatchWindow.Clamp(S(30), S(60), S(180));

        Assert.True(w.IsUsable);
        Assert.Equal(S(30), w.Start);
        Assert.Equal(S(60), w.End);
        Assert.Equal(S(30), w.Length);
    }

    [Fact]
    public void AnEndPastTheClipIsPulledBackToTheClipEnd()
    {
        // Real case from the demo library: "boss fight", 120-210s on a 178.7s clip.
        var w = WatchWindow.Clamp(S(120), S(210), S(178.7));

        Assert.True(w.IsUsable);
        Assert.Equal(S(120), w.Start);
        Assert.Equal(S(178.7), w.End);
    }

    [Fact]
    public void AStartPastTheClipMakesTheWindowUnusable()
    {
        // Real case from the demo library: "lucky escape", 180-210s on a 178.5s clip. This is the
        // range that hung the app.
        var w = WatchWindow.Clamp(S(180), S(210), S(178.5));

        Assert.False(w.IsUsable);
    }

    [Fact]
    public void AnUnusableWindowIsNeverNegative()
    {
        var w = WatchWindow.Clamp(S(180), S(210), S(178.5));

        // Start is pinned to the end rather than left past it, so nothing downstream sees a
        // negative length or seeks backwards past the media.
        Assert.True(w.Length >= TimeSpan.Zero);
        Assert.True(w.Start <= w.End);
        Assert.True(w.End <= S(178.5));
    }

    [Fact]
    public void AWindowShorterThanTheMinimumIsRejected()
    {
        // Clamping can leave a sliver at the very end of the media. A sliver is indistinguishable
        // from an instant seek to the end, which is the shape that spins.
        var w = WatchWindow.Clamp(S(178.45), S(210), S(178.5));

        Assert.False(w.IsUsable);
    }

    [Fact]
    public void AWindowExactlyAtTheMinimumIsAccepted()
    {
        var w = WatchWindow.Clamp(
            TimeSpan.Zero,
            WatchWindow.MinimumUsableLength,
            S(10));

        Assert.True(w.IsUsable);
    }

    [Fact]
    public void ANegativeStartIsPulledUpToZero()
    {
        var w = WatchWindow.Clamp(S(-5), S(20), S(180));

        Assert.True(w.IsUsable);
        Assert.Equal(TimeSpan.Zero, w.Start);
        Assert.Equal(S(20), w.End);
    }

    [Fact]
    public void ARangeEndingExactlyAtTheClipEndIsUnchanged()
    {
        var w = WatchWindow.Clamp(S(150), S(180), S(180));

        Assert.True(w.IsUsable);
        Assert.Equal(S(180), w.End);
    }

    [Fact]
    public void AnUnknownDurationLeavesTheRangeUntouched()
    {
        // Duration is not always known when the window is set; clamping to zero there would break
        // every highlight rather than the broken ones.
        var w = WatchWindow.Clamp(S(30), S(60), TimeSpan.Zero);

        Assert.True(w.IsUsable);
        Assert.Equal(S(30), w.Start);
        Assert.Equal(S(60), w.End);
    }

    [Fact]
    public void AnInvertedRangeIsRejectedRatherThanPlayedBackwards()
    {
        var w = WatchWindow.Clamp(S(90), S(60), S(180));

        Assert.False(w.IsUsable);
        Assert.True(w.Start <= w.End);
    }

    [Fact]
    public void EveryDemoLibraryHighlightClampsToSomethingPlayableOrIsRejected()
    {
        // The two out-of-range rows in the seeded demo library, plus a normal one.
        (double Start, double End, double Duration, bool Usable)[] cases =
        [
            (89, 108, 179.0, true),    // dry push
            (120, 210, 178.7, true),   // boss fight - end past the clip, still playable
            (180, 210, 178.5, false),  // lucky escape - starts past the clip
        ];

        foreach (var (start, end, duration, usable) in cases)
        {
            var w = WatchWindow.Clamp(S(start), S(end), S(duration));

            Assert.Equal(usable, w.IsUsable);
            Assert.True(w.End <= S(duration));
            Assert.True(w.Start <= w.End);
        }
    }
}
