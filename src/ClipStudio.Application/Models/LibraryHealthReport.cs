namespace ClipStudio.Application.Models;

/// <summary>
/// The result of one library health check: what it found, and what it changed.
/// </summary>
/// <remarks>
/// The check is deliberately cheap - one directory listing per source folder compared against the
/// clip paths in the database - so this report is what a launch produces instead of the full
/// repair pass that used to run there. The only writes it describes are the broken flag, which
/// records a fact about the disk rather than a decision about the library.
/// </remarks>
public sealed class LibraryHealthReport
{
    /// <summary>Gets the findings, in the order they were discovered.</summary>
    public IReadOnlyList<LibraryHealthFinding> Findings { get; init; } = [];

    /// <summary>Gets how many clips were newly marked broken because their file is gone.</summary>
    public int ClipsMarkedBroken { get; init; }

    /// <summary>Gets how many clips had a stale broken flag cleared because their file is back.</summary>
    public int BrokenFlagsCleared { get; init; }

    /// <summary>Gets how many clips were checked.</summary>
    public int ClipsChecked { get; init; }

    /// <summary>
    /// Gets how many clips were skipped because their source folder could not be reached.
    /// </summary>
    /// <remarks>
    /// These are not counted as missing. Marking a whole disconnected drive's clips broken is the
    /// behaviour this split exists to avoid.
    /// </remarks>
    public int ClipsSkippedAsUnreachable { get; init; }

    /// <summary>Gets how long the check took.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>Gets whether anything was found that needs a person to look at it.</summary>
    public bool NeedsAttention => Findings.Count > 0;

    /// <summary>
    /// Returns a one-line summary suitable for the log and for a status line.
    /// </summary>
    /// <returns>The summary text.</returns>
    public override string ToString()
    {
        if (Findings.Count == 0)
            return $"Library health check: {ClipsChecked} clip(s) checked, nothing needs attention "
                 + $"({Elapsed.TotalMilliseconds:F0} ms).";

        var unreachable = Findings.Count(f => f.Kind == LibraryHealthFindingKind.SourceFolderUnreachable);
        var missing     = Findings.Count(f => f.Kind == LibraryHealthFindingKind.ClipFileMissing);
        var unimported  = Findings
            .Where(f => f.Kind == LibraryHealthFindingKind.UnimportedFilesFound)
            .Sum(f => f.Count);

        var parts = new List<string>();
        if (unreachable > 0) parts.Add($"{unreachable} unreachable source folder(s)");
        if (missing > 0)     parts.Add($"{missing} clip file(s) missing");
        if (unimported > 0)  parts.Add($"{unimported} file(s) not yet imported");

        return $"Library health check: {ClipsChecked} clip(s) checked, "
             + string.Join(", ", parts)
             + $" ({Elapsed.TotalMilliseconds:F0} ms).";
    }
}
