namespace ClipStudio.Core.Entities;

/// <summary>
/// Represents a soft, non-hierarchical association between two tags.
/// Relations are symmetric: if Tag A is related to Tag B, Tag B is also related to Tag A.
/// </summary>
public class TagRelation
{
    /// <summary>Gets or sets the identifier of the first tag in this relation.</summary>
    public int TagId { get; set; }

    /// <summary>Gets or sets the first tag in this relation.</summary>
    public Tag Tag { get; set; } = null!;

    /// <summary>Gets or sets the identifier of the second tag in this relation.</summary>
    public int RelatedTagId { get; set; }

    /// <summary>Gets or sets the second tag in this relation.</summary>
    public Tag RelatedTag { get; set; } = null!;
}
