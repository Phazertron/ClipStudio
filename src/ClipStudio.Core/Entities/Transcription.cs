namespace ClipStudio.Core.Entities;

/// <summary>
/// Represents a completed speech-to-text transcription run for a specific clip.
/// Each transcription is produced by a local Whisper model and contains one or more
/// timed <see cref="TranscriptionSegment"/> entries suitable for export as an SRT file.
/// </summary>
public class Transcription
{
    /// <summary>Gets or sets the unique identifier of this transcription.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the identifier of the clip this transcription belongs to.</summary>
    public int ClipId { get; set; }

    /// <summary>Gets or sets the clip this transcription belongs to.</summary>
    public Clip Clip { get; set; } = null!;

    /// <summary>Gets or sets the UTC timestamp when the transcription was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the BCP-47 language code of the transcribed audio (e.g. "en", "fr"),
    /// or "auto" when Whisper detected the language automatically.
    /// </summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the Whisper model used (e.g. "tiny", "base", "small").
    /// Derived from the model filename at transcription time.
    /// </summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path to the generated SRT subtitle file on disk.
    /// May be empty if the file was deleted or the output folder is inaccessible.
    /// </summary>
    public string SrtFilePath { get; set; } = string.Empty;

    /// <summary>Gets the ordered collection of timed text segments produced by this transcription.</summary>
    public ICollection<TranscriptionSegment> Segments { get; set; } = new List<TranscriptionSegment>();
}
