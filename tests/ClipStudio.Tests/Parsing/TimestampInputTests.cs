using ClipStudio.UI.Parsing;
using Xunit;

namespace ClipStudio.Tests.Parsing;

/// <summary>
/// Unit tests for <see cref="TimestampInput"/>, the parser shared by the trim and highlight
/// range boxes.
/// </summary>
public sealed class TimestampInputTests
{
    [Theory]
    [InlineData("0:00", 0)]
    [InlineData("1:05", 65)]
    [InlineData("01:05", 65)]
    [InlineData("1:02:05", 3725)]
    public void TryParse_AcceptsTheDisplayedFormats(string input, double expectedSeconds)
    {
        Assert.True(TimestampInput.TryParse(input, out var parsed));
        Assert.Equal(expectedSeconds, parsed.TotalSeconds, 3);
    }

    [Theory]
    [InlineData("1:05.4", 65.4)]
    [InlineData("1:05.45", 65.45)]
    [InlineData("1:05.456", 65.456)]
    [InlineData("1:02:05.5", 3725.5)]
    public void TryParse_AcceptsFractionalSeconds(string input, double expectedSeconds)
    {
        Assert.True(TimestampInput.TryParse(input, out var parsed));
        Assert.Equal(expectedSeconds, parsed.TotalSeconds, 3);
    }

    [Theory]
    [InlineData("90", 90)]
    [InlineData("0", 0)]
    public void TryParse_AcceptsABareSecondCount(string input, double expectedSeconds)
    {
        Assert.True(TimestampInput.TryParse(input, out var parsed));
        Assert.Equal(expectedSeconds, parsed.TotalSeconds, 3);
    }

    [Fact]
    public void TryParse_IgnoresSurroundingWhitespace()
    {
        Assert.True(TimestampInput.TryParse("  1:05  ", out var parsed));
        Assert.Equal(65, parsed.TotalSeconds, 3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a time")]
    [InlineData("1:2:3:4")]
    [InlineData("-5")]
    public void TryParse_RejectsWhatItCannotRead(string? input)
    {
        Assert.False(TimestampInput.TryParse(input, out var parsed));
        Assert.Equal(TimeSpan.Zero, parsed);
    }
}
