using ClipStudio.Core.Enums;

namespace ClipStudio.Core.Entities;

/// <summary>
/// A relationship the user drew between two clips.
/// </summary>
/// <remarks>
/// Stored once, in the direction it was created, and read from both ends. Storing both directions
/// the way <see cref="TagRelation"/> does would double every write and make the asymmetric types
/// ambiguous: there would be no way to tell which clip is the sequel.
/// </remarks>
public class ClipLink
{
    /// <summary>Gets or sets the link's database identifier.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the identifier of the clip the link was created from.</summary>
    public int SourceClipId { get; set; }

    /// <summary>Gets or sets the clip the link was created from.</summary>
    public Clip SourceClip { get; set; } = null!;

    /// <summary>Gets or sets the identifier of the clip the link points at.</summary>
    public int TargetClipId { get; set; }

    /// <summary>Gets or sets the clip the link points at.</summary>
    public Clip TargetClip { get; set; } = null!;

    /// <summary>Gets or sets what kind of relationship this is.</summary>
    public ClipLinkType LinkType { get; set; }

    /// <summary>Gets or sets an optional note explaining the link.</summary>
    public string? Note { get; set; }

    /// <summary>Gets or sets when the link was created, in UTC.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
