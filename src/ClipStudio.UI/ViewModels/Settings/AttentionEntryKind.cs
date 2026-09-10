namespace ClipStudio.UI.ViewModels.Settings;

/// <summary>
/// The kinds of entry the attention list can hold.
/// </summary>
/// <remarks>
/// Each value is something the library cannot resolve on its own. Nothing here is fixed
/// automatically: every entry prompts, and the action it offers is the user's to take.
/// </remarks>
public enum AttentionEntryKind
{
    /// <summary>A source folder's drive is disconnected, so its clips are out of reach.</summary>
    SourceFolderUnreachable = 0,

    /// <summary>A clip's file is gone from a folder that is reachable.</summary>
    ClipFileMissing = 1,

    /// <summary>A reachable source folder holds files no clip refers to.</summary>
    UnimportedFiles = 2,

    /// <summary>
    /// A highlight's range starts past the end of its clip, so there is no correct position to
    /// move it to and it has to be re-picked by hand.
    /// </summary>
    HighlightOutOfRange = 3,

    /// <summary>Two or more clips in the library are the same recording.</summary>
    DuplicateClips = 4,

    /// <summary>
    /// A newer release of the application has been downloaded and is waiting to be installed.
    /// </summary>
    /// <remarks>
    /// Unlike the other kinds this is not a fault in the library, but it belongs to the same list
    /// for the same reason: it needs a decision from the user. Installing an update restarts the
    /// application, so it waits to be asked for rather than happening mid-session.
    /// </remarks>
    UpdateAvailable = 5,
}
