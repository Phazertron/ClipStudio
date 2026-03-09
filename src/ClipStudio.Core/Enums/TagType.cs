namespace ClipStudio.Core.Enums;

/// <summary>
/// Classifies the semantic role of a tag within the tagging system.
/// </summary>
public enum TagType
{
    /// <summary>
    /// A general-purpose user-defined tag (e.g., "Funny", "Clutch", "Kill-Streak").
    /// </summary>
    General,

    /// <summary>
    /// A tag representing a specific game title, optionally linked to an IGDB entry.
    /// </summary>
    Game
}
