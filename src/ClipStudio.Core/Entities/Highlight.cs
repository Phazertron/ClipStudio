namespace ClipStudio.Core.Entities;

/// <summary>
/// Represents a user-defined tagged time range within a clip.
/// Multiple highlights may exist per clip and may overlap freely; each is treated as fully independent.
/// A clip inherits all tags from its highlights for the purposes of search and filtering.
/// </summary>
public class Highlight
{
    /// <summary>Gets or sets the unique identifier of this highlight.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the identifier of the clip this highlight belongs to.</summary>
    public int ClipId { get; set; }

    /// <summary>Gets or sets the clip this highlight belongs to.</summary>
    public Clip Clip { get; set; } = null!;

    /// <summary>Gets or sets an optional short label for this highlight (e.g., "Clutch play").</summary>
    public string? Label { get; set; }

    /// <summary>Gets or sets optional free-text notes for this highlight.</summary>
    public string? Notes { get; set; }

    /// <summary>Gets or sets the start position of this highlight within the clip.</summary>
    public TimeSpan StartTime { get; set; }

    /// <summary>Gets or sets the end position of this highlight within the clip.</summary>
    public TimeSpan EndTime { get; set; }

    /// <summary>Gets or sets the UTC timestamp when this highlight was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the user rating for this highlight, from 0 (unrated) to 5 (best).
    /// </summary>
    public int Rating { get; set; }

    /// <summary>Gets or sets a value indicating whether the user has marked this highlight as a favourite.</summary>
    public bool IsFavorite { get; set; }

    /// <summary>Gets the collection of tags applied to this highlight.</summary>
    public ICollection<HighlightTag> HighlightTags { get; set; } = new List<HighlightTag>();

    /// <summary>Gets the duration of this highlight.</summary>
    public TimeSpan Duration => EndTime - StartTime;

    /// <summary>
    /// Gets or sets the absolute path to the thumbnail image generated for this highlight,
    /// captured at the highlight's midpoint. Null if not yet generated.
    /// </summary>
    public string? ThumbnailPath { get; set; }
}
