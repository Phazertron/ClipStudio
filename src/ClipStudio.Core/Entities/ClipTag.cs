namespace ClipStudio.Core.Entities;

/// <summary>
/// Join entity representing a tag applied directly to a clip at the clip level.
/// Clip-level tags apply to the clip as a whole, independently of any highlights.
/// </summary>
public class ClipTag
{
    /// <summary>Gets or sets the identifier of the clip this tag is applied to.</summary>
    public int ClipId { get; set; }

    /// <summary>Gets or sets the clip this tag is applied to.</summary>
    public Clip Clip { get; set; } = null!;

    /// <summary>Gets or sets the identifier of the tag applied to the clip.</summary>
    public int TagId { get; set; }

    /// <summary>Gets or sets the tag applied to the clip.</summary>
    public Tag Tag { get; set; } = null!;
}
