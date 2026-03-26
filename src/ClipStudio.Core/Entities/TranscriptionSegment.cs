namespace ClipStudio.Core.Entities;

/// <summary>
/// Represents a single timed text segment within a <see cref="Transcription"/>.
/// Each segment corresponds to one subtitle block in the generated SRT file.
/// </summary>
public class TranscriptionSegment
{
    /// <summary>Gets or sets the unique identifier of this segment.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the identifier of the parent transcription.</summary>
    public int TranscriptionId { get; set; }

    /// <summary>Gets or sets the parent transcription this segment belongs to.</summary>
    public Transcription Transcription { get; set; } = null!;

    /// <summary>Gets or sets the 1-based sequential index of this segment within the SRT file.</summary>
    public int IndexNumber { get; set; }

    /// <summary>Gets or sets the segment start position in milliseconds from the beginning of the clip.</summary>
    public long StartMs { get; set; }

    /// <summary>Gets or sets the segment end position in milliseconds from the beginning of the clip.</summary>
    public long EndMs { get; set; }

    /// <summary>Gets or sets the transcribed text content for this segment.</summary>
    public string Text { get; set; } = string.Empty;
}
