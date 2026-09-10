namespace ClipStudio.Application.Models;

/// <summary>
/// The kinds of problem a startup library health check can report.
/// </summary>
/// <remarks>
/// Every kind names something a person has to decide about. Nothing here is resolved
/// automatically, so each value maps to one entry in the attention list rather than to an action
/// the check performs on its own.
/// </remarks>
public enum LibraryHealthFindingKind
{
    /// <summary>
    /// A source folder's root could not be reached, usually because its drive is disconnected.
    /// Its clips are left untouched: they are not missing, they are merely out of reach.
    /// </summary>
    SourceFolderUnreachable = 0,

    /// <summary>
    /// A clip's row points at a file that is no longer on disk, in a folder that is reachable.
    /// The clip is marked broken so the library shows it as such.
    /// </summary>
    ClipFileMissing = 1,

    /// <summary>
    /// A reachable source folder holds video files that no clip row refers to, so a scan would
    /// import them.
    /// </summary>
    UnimportedFilesFound = 2,
}
