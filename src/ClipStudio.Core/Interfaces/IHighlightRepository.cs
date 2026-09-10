using ClipStudio.Core.Entities;
using ClipStudio.Core.Models;

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

    /// <summary>
    /// Returns every live highlight's range next to its clip's duration.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>One snapshot per highlight of a live clip.</returns>
    /// <remarks>
    /// A projection rather than <see cref="GetAllAsync"/>, which loads each highlight's tags and
    /// its clip's tags and players. Deciding whether a range still fits its clip needs neither.
    /// </remarks>
    Task<IReadOnlyList<HighlightRangeSnapshot>> GetRangeSnapshotsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds highlights whose label contains the given text.
    /// </summary>
    /// <param name="searchText">The text to look for.</param>
    /// <param name="limit">The most results to return.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The matching highlights, oldest first.</returns>
    /// <remarks>
    /// A targeted query rather than a filter over <see cref="GetRangeSnapshotsAsync"/>, because
    /// search runs as the user types and reading every highlight per keystroke would not scale.
    /// </remarks>
    Task<IReadOnlyList<HighlightRangeSnapshot>> SearchByLabelAsync(
        string searchText, int limit, CancellationToken cancellationToken = default);
}
