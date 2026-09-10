namespace ClipStudio.Core.Enums;

/// <summary>
/// The kinds of relationship one clip can have with another.
/// </summary>
/// <remarks>
/// Two of these are symmetric and two are not, which is the whole reason the type is stored on the
/// link rather than inferred. <see cref="SameMoment"/> and <see cref="Variant"/> read the same from
/// either side; <see cref="Sequel"/> and <see cref="Reaction"/> have a direction, and the clip on
/// the far end of one is shown with the opposite wording.
/// </remarks>
public enum ClipLinkType
{
    /// <summary>The same moment recorded from another angle, player or capture. Symmetric.</summary>
    SameMoment = 0,

    /// <summary>What happened next. Seen from the other end, the linked clip is what came before.</summary>
    Sequel = 1,

    /// <summary>Someone reacting to the other clip. Seen from the other end, the linked clip is what was reacted to.</summary>
    Reaction = 2,

    /// <summary>Another cut or edit of the same footage. Symmetric.</summary>
    Variant = 3,
}
