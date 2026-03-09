namespace ClipStudio.Core.Enums;

/// <summary>
/// Represents the processing state of a video export job.
/// </summary>
public enum ExportJobStatus
{
    /// <summary>
    /// The job is queued and waiting to be processed.
    /// </summary>
    Pending,

    /// <summary>
    /// The job is currently being processed by FFmpeg.
    /// </summary>
    Processing,

    /// <summary>
    /// The job completed successfully and the output file has been written.
    /// </summary>
    Completed,

    /// <summary>
    /// The job failed. Inspect the error message for details.
    /// </summary>
    Failed,

    /// <summary>
    /// The job was cancelled by the user before completion.
    /// </summary>
    Cancelled
}
