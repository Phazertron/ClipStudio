using System.Text.RegularExpressions;

namespace ClipStudio.Application.Parsing;

/// <summary>
/// Parses video filenames produced by OBS Studio, optionally augmented by the ClipStudio
/// OBS script which appends the active game name in square brackets.
/// </summary>
/// <remarks>
/// Expected filename format (OBS default with script):
/// <c>Replay 2025-03-03 22-49-45 [Apex Legends].mp4</c>
/// The bracket token is optional; files without it are still parsed for their timestamp.
/// </remarks>
public static class ClipFileNameParser
{
    private static readonly Regex GameNamePattern =
        new(@"\[([^\[\]]+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TimestampPattern =
        new(@"(\d{4}-\d{2}-\d{2})\s+(\d{2}-\d{2}-\d{2})", RegexOptions.Compiled);

    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".mov", ".webm", ".avi", ".flv", ".ts" };

    /// <summary>
    /// Attempts to extract a suggested game name from the given filename.
    /// Returns the text found inside the last pair of square brackets, or null if none is present.
    /// </summary>
    /// <param name="fileName">The filename (with or without directory path) to parse.</param>
    /// <returns>The suggested game name, or null if no bracket token was found.</returns>
    public static string? ExtractGameName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var match = GameNamePattern.Matches(name).LastOrDefault();
        return match?.Groups[1].Value.Trim();
    }

    /// <summary>
    /// Attempts to extract a <see cref="DateTime"/> from the OBS-format timestamp embedded in
    /// the filename (e.g., <c>2025-03-03 22-49-45</c>). Returns null if no timestamp is found.
    /// </summary>
    /// <param name="fileName">The filename to parse.</param>
    /// <returns>The parsed <see cref="DateTime"/> in UTC, or null if no timestamp could be parsed.</returns>
    public static DateTime? ExtractTimestamp(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var match = TimestampPattern.Match(name);
        if (!match.Success)
            return null;

        var datePart = match.Groups[1].Value;
        var timePart = match.Groups[2].Value.Replace('-', ':');

        // OBS embeds the local recording time in the filename.
        // Parse without assuming UTC so the result has DateTimeKind.Unspecified;
        // the EF UTC value-converter then calls ToUniversalTime() (treating it as local)
        // and stores the correct UTC value.  Callers that display the value should use
        // ToLocalTime() to convert back to the user's timezone.
        return DateTime.TryParse(
            $"{datePart} {timePart}",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var result)
            ? result
            : null;
    }

    /// <summary>
    /// Returns true if the given file extension is a supported video format.
    /// </summary>
    /// <param name="filePath">The file path or filename to check.</param>
    public static bool IsSupportedVideoFile(string filePath)
        => SupportedExtensions.Contains(Path.GetExtension(filePath));
}
