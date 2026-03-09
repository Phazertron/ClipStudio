using ClipStudio.Application.Models;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;

namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Provides operations for managing user-defined tags, including hierarchy, relations, and game search.
/// </summary>
public interface ITagService
{
    /// <summary>Returns all tags ordered by name.</summary>
    Task<IReadOnlyList<Tag>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all root-level tags (those with no parent), including their immediate children.</summary>
    Task<IReadOnlyList<Tag>> GetRootsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all tags of the given type.</summary>
    Task<IReadOnlyList<Tag>> GetByTypeAsync(TagType type, CancellationToken cancellationToken = default);

    /// <summary>Returns the tag with the given identifier, or null if not found.</summary>
    Task<Tag?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Creates a new general-purpose tag with the given properties.</summary>
    Task<Tag> CreateAsync(
        string name,
        string color,
        string? description = null,
        int? parentTagId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a Game-type tag linked to a Steam game entry.</summary>
    Task<Tag> CreateFromSteamAsync(SteamGame game, CancellationToken cancellationToken = default);

    /// <summary>Creates a Game-type tag with a custom name (no store linkage).</summary>
    Task<Tag> CreateCustomGameTagAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Updates the name, colour, description, and parent of an existing tag.</summary>
    Task UpdateAsync(
        int tagId,
        string name,
        string color,
        string? description,
        int? parentTagId,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a tag and removes all its clip-level and highlight-level assignments.</summary>
    Task DeleteAsync(int tagId, CancellationToken cancellationToken = default);

    /// <summary>Creates a bidirectional soft relation between two tags.</summary>
    Task AddRelationAsync(int tagId, int relatedTagId, CancellationToken cancellationToken = default);

    /// <summary>Removes the bidirectional soft relation between two tags.</summary>
    Task RemoveRelationAsync(int tagId, int relatedTagId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches the Steam game database for games matching the given query string.
    /// Returns an empty list on network error or when no results are found.
    /// </summary>
    Task<IReadOnlyList<SteamGame>> SearchGamesAsync(string query, CancellationToken cancellationToken = default);
}
