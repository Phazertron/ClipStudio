using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;

namespace ClipStudio.Core.Interfaces;

/// <summary>
/// Defines data access operations for <see cref="ClipLink"/> entities.
/// </summary>
public interface IClipLinkRepository
{
    /// <summary>
    /// Returns every link touching the given clip, from either end.
    /// </summary>
    /// <param name="clipId">The clip whose links to read.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The links, with both clips loaded.</returns>
    /// <remarks>
    /// A link is stored once, in the direction it was created, so a clip's related clips are the
    /// union of the links pointing out of it and those pointing at it.
    /// </remarks>
    Task<IReadOnlyList<ClipLink>> GetForClipAsync(int clipId, CancellationToken cancellationToken = default);

    /// <summary>Returns a single link by identifier.</summary>
    /// <param name="id">The link's identifier.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The link, or null when it does not exist.</returns>
    Task<ClipLink?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds an existing link between two clips of the given kind, in either direction.
    /// </summary>
    /// <param name="clipId">One end.</param>
    /// <param name="otherClipId">The other end.</param>
    /// <param name="linkType">The kind of relationship.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The existing link, or null.</returns>
    /// <remarks>
    /// Checked in both directions so that linking A to B and then B to A does not produce two rows
    /// describing one relationship - the unique index only covers the stored direction.
    /// </remarks>
    Task<ClipLink?> FindAsync(
        int clipId, int otherClipId, ClipLinkType linkType, CancellationToken cancellationToken = default);

    /// <summary>Adds a link.</summary>
    /// <param name="link">The link to add.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    Task AddAsync(ClipLink link, CancellationToken cancellationToken = default);

    /// <summary>Removes a link by identifier. No-ops when it does not exist.</summary>
    /// <param name="id">The link's identifier.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Counts the links touching each of the given clips, from either end.</summary>
    /// <param name="clipIds">The clips to count for.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>Clip identifier to link count, omitting clips with none.</returns>
    Task<IReadOnlyDictionary<int, int>> CountByClipAsync(
        IEnumerable<int> clipIds, CancellationToken cancellationToken = default);
}
