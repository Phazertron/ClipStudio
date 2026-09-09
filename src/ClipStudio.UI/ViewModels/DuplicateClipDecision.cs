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
}
