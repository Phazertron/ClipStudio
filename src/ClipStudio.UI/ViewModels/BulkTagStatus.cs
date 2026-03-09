namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Describes the status of a tag chip shown in the bulk-edit panel when multiple clips are selected.
/// </summary>
public enum BulkTagStatus
{
    /// <summary>The tag is present on every selected clip. Shown in purple; no action required for Apply.</summary>
    Shared,

    /// <summary>
    /// The tag is present on some but not all selected clips.
    /// Shown in red. The user must click the chip to promote it to <see cref="New"/>
    /// before <c>ApplyBulkEditsCommand</c> will add it to the remaining clips.
    /// </summary>
    Partial,

    /// <summary>
    /// The tag was explicitly added or promoted by the user during this bulk session.
    /// Shown in yellow. Apply will add this tag to every selected clip that does not already have it.
    /// </summary>
    New,
}