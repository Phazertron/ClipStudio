namespace ClipStudio.Core.Enums;

/// <summary>
/// Determines whether a trim or export operation modifies the original source file.
/// </summary>
public enum TrimMode
{
    /// <summary>
    /// Only in/out point metadata is stored. The original source file is never modified.
    /// A new file is produced only when the user explicitly requests an export.
    /// </summary>
    NonDestructive,

    /// <summary>
    /// FFmpeg produces a new output file immediately. The original source may optionally
    /// be deleted afterward, subject to the user's retention setting.
    /// </summary>
    Destructive
}
