using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;

namespace ClipStudio.Core.Interfaces;

/// <summary>
/// Defines data access operations for <see cref="Tag"/> entities, including hierarchy and relations.
/// </summary>
public interface ITagRepository
{
    /// <summary>Returns the tag with the given identifier, including its children and relations, or null if not found.</summary>
    Task<Tag?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Returns all tags of the given type.</summary>
    Task<IReadOnlyList<Tag>> GetByTypeAsync(TagType type, CancellationToken cancellationToken = default);

    /// <summary>Returns all root-level tags (those with no parent).</summary>
    Task<IReadOnlyList<Tag>> GetRootsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all tags, ordered by name.</summary>
    Task<IReadOnlyList<Tag>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all descendant tag identifiers for the given tag, recursively.</summary>
    Task<IReadOnlyList<int>> GetDescendantIdsAsync(int tagId, CancellationToken cancellationToken = default);

    /// <summary>Adds a new tag to the repository.</summary>
    Task AddAsync(Tag tag, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing tag.</summary>
    Task UpdateAsync(Tag tag, CancellationToken cancellationToken = default);

    /// <summary>Removes a tag and all its assignments by its identifier.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Adds a soft relation between two tags.</summary>
    Task AddRelationAsync(int tagId, int relatedTagId, CancellationToken cancellationToken = default);

    /// <summary>Removes the soft relation between two tags.</summary>
    Task RemoveRelationAsync(int tagId, int relatedTagId, CancellationToken cancellationToken = default);
}
