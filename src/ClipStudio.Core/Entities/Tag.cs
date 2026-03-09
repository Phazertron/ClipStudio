using ClipStudio.Core.Enums;

namespace ClipStudio.Core.Entities;

/// <summary>
/// Represents a user-defined tag that can be applied to clips and highlights.
/// Tags support a single-parent hierarchy and many-to-many soft relations between sibling tags.
/// </summary>
public class Tag
{
    /// <summary>Gets or sets the unique identifier of this tag.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the display name of the tag (e.g., "Funny", "Kill-Streak").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the hex colour code used to display this tag in the UI (e.g., "#E74C3C").</summary>
    public string Color { get; set; } = "#607D8B";

    /// <summary>Gets or sets an optional icon identifier or path associated with this tag.</summary>
    public string? Icon { get; set; }

    /// <summary>Gets or sets an optional description of when and how to use this tag.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the semantic type of this tag.</summary>
    public TagType Type { get; set; } = TagType.General;

    /// <summary>
    /// Gets or sets the identifier of the parent tag in the hierarchy, if any.
    /// A null value indicates this is a root-level tag.
    /// </summary>
    public int? ParentTagId { get; set; }

    /// <summary>Gets or sets the parent tag in the hierarchy.</summary>
    public Tag? ParentTag { get; set; }

    /// <summary>Gets the collection of child tags that have this tag as their parent.</summary>
    public ICollection<Tag> ChildTags { get; set; } = new List<Tag>();

    /// <summary>
    /// Gets or sets the Steam App ID for Game-type tags linked to a Steam game.
    /// </summary>
    public long? GameStoreAppId { get; set; }

    /// <summary>
    /// Gets or sets the URL of the cover art image for Game-type tags (sourced from Steam CDN).
    /// </summary>
    public string? GameCoverUrl { get; set; }

    /// <summary>
    /// Gets the collection of soft relations linking this tag to other related tags
    /// that do not share a parent-child relationship.
    /// </summary>
    public ICollection<TagRelation> Relations { get; set; } = new List<TagRelation>();

    /// <summary>Gets the collection of clip-level tag assignments for this tag.</summary>
    public ICollection<ClipTag> ClipTags { get; set; } = new List<ClipTag>();

    /// <summary>Gets the collection of highlight-level tag assignments for this tag.</summary>
    public ICollection<HighlightTag> HighlightTags { get; set; } = new List<HighlightTag>();
}
