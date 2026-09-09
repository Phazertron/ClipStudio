namespace ClipStudio.UI.ViewModels;

/// <summary>
/// What came of resolving a scan's duplicates.
/// </summary>
/// <param name="Imported">How many duplicates the user chose to import anyway.</param>
/// <param name="Skipped">How many were left out of the library.</param>
public sealed record DuplicateResolutionSummary(int Imported, int Skipped)
{
    /// <summary>Gets the total number of duplicates that were resolved.</summary>
    public int Total => Imported + Skipped;
}
