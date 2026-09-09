using ClipStudio.Core.Entities;
using ClipStudio.UI.ViewModels;

namespace ClipStudio.Tests.ViewModels;

/// <summary>
/// Covers stepping through a watch queue past highlights that cannot be played, which happens when
/// a clip is relocated to a shorter file or trimmed after its highlights were written.
/// </summary>
public class HighlightQueueTests
{
    /// <summary>Builds a row whose range either fits its clip or falls outside it.</summary>
    /// <param name="id">The highlight identifier.</param>
    /// <param name="playable">Whether the range should fit inside the clip.</param>
    /// <returns>The row.</returns>
    private static HighlightRowViewModel Row(int id, bool playable)
    {
        var clip = new Clip
        {
            Id       = id,
            FileName = $"clip{id}.mp4",
            Duration = TimeSpan.FromSeconds(180),
        };

        // An unplayable row starts after its clip has already ended.
        var start = playable ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(200);

        return new HighlightRowViewModel(new Highlight
        {
            Id        = id,
            ClipId    = id,
            Clip      = clip,
            Label     = $"h{id}",
            StartTime = start,
            EndTime   = start + TimeSpan.FromSeconds(20),
        });
    }

    private static List<HighlightRowViewModel> Rows(params bool[] playable)
        => playable.Select((p, i) => Row(i + 1, p)).ToList();

    [Fact]
    public void APlayableRowMarksItselfPlayable()
    {
        Assert.False(Row(1, playable: true).IsOutOfRange);
        Assert.True(Row(1, playable: false).IsOutOfRange);
    }

    [Fact]
    public void AnOutOfRangeRowCarriesAnExplanation()
    {
        var row = Row(1, playable: false);

        Assert.NotNull(row.OutOfRangeMessage);
        Assert.Contains("clip ends", row.OutOfRangeMessage);
    }

    [Fact]
    public void ForwardsMovesToTheNextRow()
    {
        var rows = Rows(true, true, true);

        Assert.Equal(1, HighlightQueue.FindPlayable(rows, 0, step: 1));
    }

    [Fact]
    public void ForwardsSkipsAnUnplayableRow()
    {
        var rows = Rows(true, false, true);

        Assert.Equal(2, HighlightQueue.FindPlayable(rows, 0, step: 1));
    }

    [Fact]
    public void ForwardsSkipsSeveralUnplayableRowsInARow()
    {
        var rows = Rows(true, false, false, false, true);

        Assert.Equal(4, HighlightQueue.FindPlayable(rows, 0, step: 1));
    }

    [Fact]
    public void ForwardsWrapsAroundTheEnd()
    {
        var rows = Rows(true, true, false);

        // From the last row, wrap past the unplayable one back to the start.
        Assert.Equal(0, HighlightQueue.FindPlayable(rows, 1, step: 1));
    }

    [Fact]
    public void BackwardsSkipsAnUnplayableRow()
    {
        var rows = Rows(true, false, true);

        Assert.Equal(0, HighlightQueue.FindPlayable(rows, 2, step: -1));
    }

    [Fact]
    public void BackwardsWrapsAroundTheStart()
    {
        var rows = Rows(true, true, true);

        Assert.Equal(2, HighlightQueue.FindPlayable(rows, 0, step: -1));
    }

    [Fact]
    public void ASequenceWithNothingElsePlayableReturnsNoIndex()
    {
        // The one being watched plus two broken ones: LoopAll must not spin looking for a fourth.
        var rows = Rows(true, false, false);

        Assert.Equal(-1, HighlightQueue.FindPlayable(rows, 0, step: 1));
    }

    [Fact]
    public void ASequenceOfOneReturnsNoIndex()
    {
        var rows = Rows(true);

        Assert.Equal(-1, HighlightQueue.FindPlayable(rows, 0, step: 1));
        Assert.Equal(-1, HighlightQueue.FindPlayable(rows, 0, step: -1));
    }

    [Fact]
    public void AnEmptySequenceReturnsNoIndex()
        => Assert.Equal(-1, HighlightQueue.FindPlayable([], 0, step: 1));

    [Fact]
    public void SteppingFromAnUnplayableRowStillFindsAPlayableOne()
    {
        // Reachable when a queue was opened before the clip was shortened.
        var rows = Rows(false, false, true);

        Assert.Equal(2, HighlightQueue.FindPlayable(rows, 0, step: 1));
    }
}
