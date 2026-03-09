using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;

namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Manages video export jobs: queuing, processing, and reporting progress.
/// </summary>
public interface IExportService
{
    /// <summary>
    /// Queues an export job for a full clip or a specific highlight range.
    /// </summary>
    /// <param name="clipId">The identifier of the clip to export.</param>
    /// <param name="highlightId">
    /// The identifier of the highlight to export, or null to export the full clip.
    /// </param>
    /// <param name="outputPath">The absolute path where the output file will be written.</param>
    /// <param name="trimMode">Whether the export is destructive or non-destructive.</param>
    /// <param name="deleteOriginal">
    /// When true and <paramref name="trimMode"/> is <see cref="TrimMode.Destructive"/>,
    /// the source file is deleted after successful export.
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The created <see cref="ExportJob"/> entity.</returns>
    Task<ExportJob> QueueAsync(
        int clipId,
        int? highlightId,
        string outputPath,
        TrimMode trimMode,
        bool deleteOriginal,
        TimeSpan? startTime = null,
        TimeSpan? endTime = null,
        CancellationToken cancellationToken = default);

    /// <summary>Processes all pending export jobs in the queue sequentially.</summary>
    Task ProcessQueueAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all export jobs across all clips, ordered by creation date descending.</summary>
    Task<IReadOnlyList<ExportJob>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all export jobs for the given clip.</summary>
    Task<IReadOnlyList<ExportJob>> GetJobsByClipAsync(int clipId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a pending export job. Has no effect if the job is already processing,
    /// completed, failed, or cancelled.
    /// </summary>
    /// <param name="jobId">The identifier of the job to cancel.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task CancelJobAsync(int jobId, CancellationToken cancellationToken = default);
}
