using System;

namespace ClipStudio.UI.Parsing;

/// <summary>
/// Parses the timestamps the user types into the trim and highlight range boxes.
/// </summary>
/// <remarks>
/// Shared by the trim editor and the highlight editor, which accept the same formats: the
/// <c>m:ss</c> and <c>h:mm:ss</c> shapes the timeline displays, each optionally carrying up to
/// three decimal places, plus a bare number of seconds as a convenience.
/// </remarks>
public static class TimestampInput
{
    private static readonly string[] AcceptedFormats =
    [
        @"m\:ss",      @"mm\:ss",      @"h\:mm\:ss",
        @"m\:ss\.f",   @"mm\:ss\.f",   @"h\:mm\:ss\.f",
        @"m\:ss\.ff",  @"mm\:ss\.ff",  @"h\:mm\:ss\.ff",
        @"m\:ss\.fff", @"mm\:ss\.fff", @"h\:mm\:ss\.fff",
    ];

    /// <summary>
    /// Attempts to parse a user-entered timestamp.
    /// </summary>
    /// <param name="input">The raw text, which may be null, empty or malformed.</param>
    /// <param name="result">
    /// The parsed value, or <see cref="TimeSpan.Zero"/> when the text could not be parsed.
    /// </param>
    /// <returns><see langword="true"/> when the text was parsed.</returns>
    public static bool TryParse(string? input, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var trimmed = input.Trim();

        if (TimeSpan.TryParseExact(trimmed, AcceptedFormats, null, out result))
            return true;

        // Fallback: a bare number of seconds. Negatives are rejected - a clip position is never
        // before its start, and letting one through put the timeline handle off the canvas.
        if (int.TryParse(trimmed, out var seconds) && seconds >= 0)
        {
            result = TimeSpan.FromSeconds(seconds);
            return true;
        }

        return false;
    }
}
