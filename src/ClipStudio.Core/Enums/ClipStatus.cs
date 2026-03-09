namespace ClipStudio.Core.Enums;

/// <summary>
/// Represents the review lifecycle state of a clip in the library.
/// </summary>
public enum ClipStatus
{
    /// <summary>
    /// Clip has been imported but not yet reviewed or tagged by the user.
    /// </summary>
    Unreviewed,

    /// <summary>
    /// Clip has been reviewed and tagged by the user.
    /// </summary>
    Reviewed,

    /// <summary>
    /// Clip has been archived and is hidden from the default library view.
    /// </summary>
    Archived
}
