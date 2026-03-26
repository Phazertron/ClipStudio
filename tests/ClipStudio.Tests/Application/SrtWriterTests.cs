using ClipStudio.Application.Services;
using ClipStudio.Core.Entities;

namespace ClipStudio.Tests.Application;

/// <summary>
/// Unit tests for <see cref="SrtWriter"/> covering timestamp formatting,
/// segment ordering, and output structure.
/// </summary>
public sealed class SrtWriterTests
{
    // ---- Timestamp formatting ----

    [Fact]
    public void Write_SingleSegment_StartsWithIndexNumber()
    {
        var segments = new[]
        {
            new TranscriptionSegment { IndexNumber = 1, StartMs = 0, EndMs = 1000, Text = "Hello" }
        };

        var srt = SrtWriter.Write(segments);

        Assert.StartsWith("1\r\n", srt);
    }

    [Fact]
    public void Write_Timestamps_FormattedAsHhMmSsCommaFff()
    {
        var segments = new[]
        {
            new TranscriptionSegment { IndexNumber = 1, StartMs = 3_661_042, EndMs = 3_665_500, Text = "X" }
        };

        var srt = SrtWriter.Write(segments);

        Assert.Contains("01:01:01,042 --> 01:01:05,500", srt);
    }

    [Fact]
    public void Write_ZeroStart_FormatsAsAllZeros()
    {
        var segments = new[]
        {
            new TranscriptionSegment { IndexNumber = 1, StartMs = 0, EndMs = 500, Text = "X" }
        };

        var srt = SrtWriter.Write(segments);

        Assert.Contains("00:00:00,000 --> 00:00:00,500", srt);
    }

    [Fact]
    public void Write_MillisecondsOnly_NoCarryover()
    {
        var segments = new[]
        {
            new TranscriptionSegment { IndexNumber = 1, StartMs = 999, EndMs = 1001, Text = "X" }
        };

        var srt = SrtWriter.Write(segments);

        Assert.Contains("00:00:00,999 --> 00:00:01,001", srt);
    }

    // ---- Multiple segments ----

    [Fact]
    public void Write_MultipleSegments_AllIncluded()
    {
        var segments = new[]
        {
            new TranscriptionSegment { IndexNumber = 1, StartMs = 0,    EndMs = 2000, Text = "First"  },
            new TranscriptionSegment { IndexNumber = 2, StartMs = 2000, EndMs = 4000, Text = "Second" },
            new TranscriptionSegment { IndexNumber = 3, StartMs = 4000, EndMs = 6000, Text = "Third"  },
        };

        var srt = SrtWriter.Write(segments);

        Assert.Contains("First",  srt);
        Assert.Contains("Second", srt);
        Assert.Contains("Third",  srt);
    }

    [Fact]
    public void Write_MultipleSegments_IndexNumbersInOrder()
    {
        var segments = new[]
        {
            new TranscriptionSegment { IndexNumber = 1, StartMs = 0,    EndMs = 1000, Text = "A" },
            new TranscriptionSegment { IndexNumber = 2, StartMs = 1000, EndMs = 2000, Text = "B" },
        };

        var srt = SrtWriter.Write(segments);

        var firstIndex  = srt.IndexOf("1\r\n", StringComparison.Ordinal);
        var secondIndex = srt.IndexOf("2\r\n", StringComparison.Ordinal);
        Assert.True(firstIndex < secondIndex);
    }

    [Fact]
    public void Write_EmptySegments_ReturnsEmptyString()
    {
        var srt = SrtWriter.Write(System.Array.Empty<TranscriptionSegment>());

        Assert.Equal(string.Empty, srt);
    }

    // ---- Text trimming ----

    [Fact]
    public void Write_TextWithLeadingTrailingWhitespace_IsTrimmed()
    {
        var segments = new[]
        {
            new TranscriptionSegment { IndexNumber = 1, StartMs = 0, EndMs = 1000, Text = "  hello world  " }
        };

        var srt = SrtWriter.Write(segments);

        Assert.Contains("\r\nhello world\r\n", srt);
    }

    // ---- Block separation ----

    [Fact]
    public void Write_Segments_AreTerminatedByBlankLine()
    {
        var segments = new[]
        {
            new TranscriptionSegment { IndexNumber = 1, StartMs = 0, EndMs = 1000, Text = "A" },
            new TranscriptionSegment { IndexNumber = 2, StartMs = 1000, EndMs = 2000, Text = "B" },
        };

        var srt = SrtWriter.Write(segments);

        // Each block ends with two consecutive newlines (blank separator line)
        Assert.Contains("A\r\n\r\n", srt);
        Assert.Contains("B\r\n\r\n", srt);
    }
}
