using System.Collections.Generic;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Navigation over an ordered list of highlights being watched one after another.
/// </summary>
public static class HighlightQueue
{
    /// <summary>
    /// Finds the next highlight that can actually be played, wrapping around the end of the list.
    /// </summary>
    /// <remarks>
    /// A highlight whose range falls outside its clip has nothing to play, so a queue that stepped
    /// onto one would stall there - and under LoopAll, keep stalling on it every lap. Skipping keeps
    /// the queue moving; the row stays in the list, marked, so it can be given a new range.
    /// </remarks>
    /// <param name="sequence">The ordered highlight rows.</param>
    /// <param name="fromIndex">The index currently being watched.</param>
    /// <param name="step">1 to move forwards, -1 to move backwards.</param>
    /// <returns>
    /// The index of the next playable highlight, or -1 when the sequence contains no other one.
    /// </returns>
    public static int FindPlayable(
        IReadOnlyList<HighlightRowViewModel> sequence, int fromIndex, int step)
    {
        var count = sequence.Count;
        if (count == 0 || step == 0) return -1;

        // Walk at most one full lap, so a sequence with nothing playable terminates rather than
        // looping forever looking for a row that is not there.
        for (var i = 1; i <= count; i++)
        {
            var index = ((fromIndex + (step * i)) % count + count) % count;
            if (index == fromIndex) break;
            if (!sequence[index].IsOutOfRange) return index;
        }

        return -1;
    }
}
