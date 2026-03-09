namespace ClipStudio.Core.Entities;

/// <summary>
/// Join entity representing a tag applied to a specific highlight time range within a clip.
/// </summary>
public class HighlightTag
{
    /// <summary>Gets or sets the identifier of the highlight this tag is applied to.</summary>
    public int HighlightId { get; set; }

    /// <summary>Gets or sets the highlight this tag is applied to.</summary>
    public Highlight Highlight { get; set; } = null!;

    /// <summary>Gets or sets the identifier of the tag applied to the highlight.</summary>
    public int TagId { get; set; }

    /// <summary>Gets or sets the tag applied to the highlight.</summary>
    public Tag Tag { get; set; } = null!;
}
