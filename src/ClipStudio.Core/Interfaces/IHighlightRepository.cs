using ClipStudio.Core.Entities;

namespace ClipStudio.Core.Interfaces;

/// <summary>
/// Defines data access operations for <see cref="Highlight"/> entities.
/// </summary>
public interface IHighlightRepository
{
    /// <summary>Returns the highlight with the given identifier, including its tags, or null if not found.</summary>
    Task<Highlight?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Returns all highlights across all clips, ordered by creation date descending.</summary>
    Task<IReadOnlyList<Highlight>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all highlights defined within the given clip.</summary>
    Task<IReadOnlyList<Highlight>> GetByClipAsync(int clipId, CancellationToken cancellationToken = default);

    /// <summary>Adds a new highlight to the repository.</summary>
    Task AddAsync(Highlight highlight, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing highlight.</summary>
    Task UpdateAsync(Highlight highlight, CancellationToken cancellationToken = default);

    /// <summary>Removes a highlight by its identifier.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
