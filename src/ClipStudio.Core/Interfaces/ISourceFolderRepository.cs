using ClipStudio.Core.Entities;

namespace ClipStudio.Core.Interfaces;

/// <summary>
/// Defines data access operations for <see cref="SourceFolder"/> entities.
/// </summary>
public interface ISourceFolderRepository
{
    /// <summary>Returns all configured source folders.</summary>
    Task<IReadOnlyList<SourceFolder>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns only active (currently watched) source folders.</summary>
    Task<IReadOnlyList<SourceFolder>> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the source folder with the given identifier, or null if not found.</summary>
    Task<SourceFolder?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Adds a new source folder to the repository.</summary>
    Task AddAsync(SourceFolder folder, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing source folder.</summary>
    Task UpdateAsync(SourceFolder folder, CancellationToken cancellationToken = default);

    /// <summary>Removes a source folder by its identifier. Associated clips are not deleted.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
