using ClipStudio.Core.Entities;

namespace ClipStudio.Core.Interfaces;

/// <summary>
/// Defines data access operations for <see cref="Screenshot"/> entities.
/// </summary>
public interface IScreenshotRepository
{
    /// <summary>Returns the screenshot with the given identifier, or null if not found.</summary>
    Task<Screenshot?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Returns all screenshots captured from the given clip, ordered by playback timestamp.</summary>
    Task<IReadOnlyList<Screenshot>> GetByClipAsync(int clipId, CancellationToken cancellationToken = default);

    /// <summary>Adds a new screenshot record to the repository.</summary>
    Task AddAsync(Screenshot screenshot, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing screenshot record.</summary>
    Task UpdateAsync(Screenshot screenshot, CancellationToken cancellationToken = default);

    /// <summary>Removes a screenshot record by its identifier. The file on disk is not deleted by this operation.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
