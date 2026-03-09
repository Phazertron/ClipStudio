using ClipStudio.Core.Entities;

namespace ClipStudio.Core.Interfaces;

/// <summary>
/// Persistence operations for <see cref="ExportJob"/> entities.
/// </summary>
public interface IExportJobRepository
{
    /// <summary>Persists a new export job and returns it with its assigned identifier.</summary>
    /// <param name="job">The export job to add.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The saved <see cref="ExportJob"/> with its database-assigned <c>Id</c>.</returns>
    Task<ExportJob> AddAsync(ExportJob job, CancellationToken cancellationToken = default);

    /// <summary>Persists changes to an existing export job (e.g., status updates).</summary>
    /// <param name="job">The export job to update.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task UpdateAsync(ExportJob job, CancellationToken cancellationToken = default);

    /// <summary>Returns all export jobs, ordered by creation date descending.</summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task<IReadOnlyList<ExportJob>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all export jobs for the given clip, ordered by creation date descending.</summary>
    /// <param name="clipId">The clip identifier to filter by.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task<IReadOnlyList<ExportJob>> GetByClipAsync(int clipId, CancellationToken cancellationToken = default);

    /// <summary>Returns all pending export jobs, ordered by creation date ascending (FIFO).</summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task<IReadOnlyList<ExportJob>> GetPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the export job with the given identifier, or null if not found.</summary>
    /// <param name="id">The job identifier.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task<ExportJob?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
}
