using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;

namespace ClipStudio.Core.Interfaces;

/// <summary>
/// Defines data access operations for <see cref="Clip"/> entities.
/// </summary>
public interface IClipRepository
{
    /// <summary>Returns the clip with the given identifier, including its tags and highlights, or null if not found.</summary>
    Task<Clip?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Returns all clips that match the given status filter.</summary>
    Task<IReadOnlyList<Clip>> GetByStatusAsync(ClipStatus status, CancellationToken cancellationToken = default);

    /// <summary>Returns all clips, ordered by creation date descending.</summary>
    Task<IReadOnlyList<Clip>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all clips that have at least one tag or highlight matching any of the given tag identifiers.</summary>
    Task<IReadOnlyList<Clip>> GetByTagsAsync(IEnumerable<int> tagIds, CancellationToken cancellationToken = default);

    /// <summary>Adds a new clip to the repository.</summary>
    Task AddAsync(Clip clip, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing clip.</summary>
    Task UpdateAsync(Clip clip, CancellationToken cancellationToken = default);

    /// <summary>
    /// Directly inserts a <see cref="ClipTag"/> row for the given clip and tag.
    /// Idempotent — silently does nothing if the assignment already exists.
    /// </summary>
    Task AddClipTagAsync(int clipId, int tagId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Directly removes the <see cref="ClipTag"/> row for the given clip and tag.
    /// Idempotent — silently does nothing if the assignment does not exist.
    /// </summary>
    Task RemoveClipTagAsync(int clipId, int tagId, CancellationToken cancellationToken = default);

    /// <summary>Removes a clip from the repository by its identifier.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Returns true if a clip with the given file path already exists in the library.</summary>
    Task<bool> ExistsByFilePathAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>Returns all clips that have been moved to the trash (IsDeleted == true), ordered by deletion date descending.</summary>
    Task<IReadOnlyList<Clip>> GetTrashedAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all clips (including soft-deleted) associated with the given source folder.</summary>
    Task<IReadOnlyList<Clip>> GetBySourceFolderIdAsync(int folderId, CancellationToken cancellationToken = default);
}
