namespace ClipStudio.Core.Entities;

/// <summary>
/// Represents a still-image frame captured from a clip at a specific playback position.
/// Screenshots are saved as PNG files and remain linked to their source clip and timestamp.
/// </summary>
public class Screenshot
{
    /// <summary>Gets or sets the unique identifier of this screenshot.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the identifier of the clip this screenshot was captured from.</summary>
    public int ClipId { get; set; }

    /// <summary>Gets or sets the clip this screenshot was captured from.</summary>
    public Clip Clip { get; set; } = null!;

    /// <summary>Gets or sets the absolute path to the PNG file on disk.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC timestamp when this screenshot was captured.</summary>
    public DateTime CapturedAt { get; set; }

    /// <summary>
    /// Gets or sets the playback position within the source clip at the moment of capture.
    /// </summary>
    public TimeSpan PlaybackTimestamp { get; set; }

    /// <summary>Gets or sets optional free-text notes added by the user for this screenshot.</summary>
    public string? Notes { get; set; }
}
