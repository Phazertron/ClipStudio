namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Represents the user's chosen action when removing a source folder that still has associated clips in the library.
/// </summary>
public enum SourceFolderRemovalResult
{
    /// <summary>The removal was cancelled; no changes are made.</summary>
    Cancel,

    /// <summary>
    /// All clips from the folder are soft-deleted (hidden from the library) but their records,
    /// tags, highlights, and source files on disk are preserved.
    /// </summary>
    Archive,

    /// <summary>
    /// All clip database records and their media-cache files (thumbnails, preview strips) are deleted.
    /// Source video files on disk are not touched.
    /// </summary>
    Wipe,
}
