using ClipStudio.Core.Enums;

namespace ClipStudio.Core.Entities;

/// <summary>
/// Represents a video export operation, either for a full clip or a specific highlight range.
/// Export jobs are processed asynchronously by FFmpeg.
/// </summary>
public class ExportJob
{
    /// <summary>Gets or sets the unique identifier of this export job.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the identifier of the clip being exported.</summary>
    public int ClipId { get; set; }

    /// <summary>Gets or sets the clip being exported.</summary>
    public Clip Clip { get; set; } = null!;

    /// <summary>
    /// Gets or sets the identifier of the highlight to export.
    /// When null, the full clip is exported (subject to any trim in/out points).
    /// </summary>
    public int? HighlightId { get; set; }

    /// <summary>Gets or sets the highlight to export, if this is a highlight-scoped export.</summary>
    public Highlight? Highlight { get; set; }

    /// <summary>Gets or sets the trim mode applied to this export job.</summary>
    public TrimMode TrimMode { get; set; }

    /// <summary>Gets or sets the absolute path where the output file will be written.</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the current processing status of this export job.</summary>
    public ExportJobStatus Status { get; set; } = ExportJobStatus.Pending;

    /// <summary>Gets or sets an error message when the job status is Failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Gets or sets the UTC timestamp when this export job was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Gets or sets the UTC timestamp when this export job completed or failed.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets whether the original source file should be moved to the trash after a successful
    /// destructive export. Only relevant when TrimMode is Destructive.
    /// </summary>
    public bool DeleteOriginalAfterExport { get; set; } = false;

    /// <summary>
    /// Gets or sets the explicit trim start time for a manual trim export.
    /// When null and <see cref="HighlightId"/> is also null, the full clip is exported.
    /// </summary>
    public TimeSpan? StartTime { get; set; }

    /// <summary>
    /// Gets or sets the explicit trim end time for a manual trim export.
    /// When null and <see cref="HighlightId"/> is also null, the full clip is exported.
    /// </summary>
    public TimeSpan? EndTime { get; set; }
}
