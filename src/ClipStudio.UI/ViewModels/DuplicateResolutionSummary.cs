namespace ClipStudio.UI.ViewModels;

/// <summary>
/// What came of resolving a scan's duplicates.
/// </summary>
/// <param name="Imported">How many duplicates the user chose to import anyway.</param>
/// <param name="Skipped">How many were left out of the library.</param>
/// <param name="Linked">
/// How many of the imported ones were also linked to the clip they duplicate. A subset of
/// <paramref name="Imported"/>, not an addition to it.
/// </param>
public sealed record DuplicateResolutionSummary(int Imported, int Skipped, int Linked = 0)
{
    /// <summary>Gets the total number of duplicates that were resolved.</summary>
    public int Total => Imported + Skipped;
}
