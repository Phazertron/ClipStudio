using ClipStudio.Core.Entities;

namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Provides operations for creating, editing, and tagging highlight time ranges within clips.
/// </summary>
public interface IHighlightService
{
    /// <summary>Returns all highlights across all clips, ordered by creation date descending.</summary>
    Task<IReadOnlyList<Highlight>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all highlights for the given clip, ordered by start time.</summary>
    Task<IReadOnlyList<Highlight>> GetByClipAsync(int clipId, CancellationToken cancellationToken = default);

    /// <summary>Returns the highlight with the given identifier, or null if not found.</summary>
    Task<Highlight?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new highlight on the given clip. Overlapping time ranges with existing highlights are permitted.
    /// </summary>
    Task<Highlight> CreateAsync(
        int clipId,
        TimeSpan startTime,
        TimeSpan endTime,
        string? label = null,
        string? notes = null,
        CancellationToken cancellationToken = default);

    /// <summary>Updates the time range, label, and notes of an existing highlight.</summary>
    Task UpdateAsync(
        int highlightId,
        TimeSpan startTime,
        TimeSpan endTime,
        string? label,
        string? notes,
        CancellationToken cancellationToken = default);

    /// <summary>Applies a tag to a highlight. A highlight may have multiple tags simultaneously.</summary>
    Task AddTagAsync(int highlightId, int tagId, CancellationToken cancellationToken = default);

    /// <summary>Removes a tag from a highlight.</summary>
    Task RemoveTagAsync(int highlightId, int tagId, CancellationToken cancellationToken = default);

    /// <summary>Sets the star rating of the given highlight (0–5).</summary>
    Task SetRatingAsync(int highlightId, int rating, CancellationToken cancellationToken = default);

    /// <summary>Toggles the IsFavorite flag on the given highlight.</summary>
    Task ToggleFavoriteAsync(int highlightId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a highlight and all its tag assignments.</summary>
    Task DeleteAsync(int highlightId, CancellationToken cancellationToken = default);
}
