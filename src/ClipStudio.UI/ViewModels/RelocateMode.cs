namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Determines what the Relocate Clip dialog does when the user confirms.
/// </summary>
public enum RelocateMode
{
    /// <summary>
    /// Physically move the file from its current location back to the original path so the
    /// existing database record stays valid without any changes.
    /// </summary>
    MoveBack,

    /// <summary>
    /// Update the database record to point at the new path and register the new folder
    /// as a watched source so the clip remains fully managed.
    /// </summary>
    RegisterNewSource,
}
