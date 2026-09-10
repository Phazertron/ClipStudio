namespace ClipStudio.UI.ViewModels;

/// <summary>
/// What the user chose to do with a file whose contents are already in the library.
/// </summary>
public enum DuplicateClipDecision
{
    /// <summary>Leave the file where it is and do not add it to the library.</summary>
    Skip,

    /// <summary>Import the file anyway, accepting a second copy of the same recording.</summary>
    ImportAnyway,

    /// <summary>
    /// Import the file anyway and link it to the clip it duplicates, so the two are findable from
    /// each other rather than sitting in the library as unrelated near-identical entries.
    /// </summary>
    ImportAndLink,
}
