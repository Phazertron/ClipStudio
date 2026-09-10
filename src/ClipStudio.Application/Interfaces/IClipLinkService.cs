using ClipStudio.Application.Models;
using ClipStudio.Core.Enums;

namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Creates and reads the relationships the user draws between clips.
/// </summary>
public interface IClipLinkService
{
    /// <summary>
    /// Returns the clips related to the given one, as seen from its side.
    /// </summary>
    /// <param name="clipId">The clip to read links for.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The related clips, oldest link first.</returns>
    Task<IReadOnlyList<RelatedClip>> GetRelatedAsync(
        int clipId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Links two clips, or returns the existing link when they are already linked that way.
    /// </summary>
    /// <param name="sourceClipId">The clip the link is created from.</param>
    /// <param name="targetClipId">The clip the link points at.</param>
    /// <param name="linkType">The kind of relationship.</param>
    /// <param name="note">An optional note explaining the link.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The link's identifier.</returns>
    /// <exception cref="ArgumentException">A clip cannot be linked to itself.</exception>
    /// <remarks>
    /// Linking twice is a no-op rather than an error: the user asked for a relationship that
    /// already exists, and they got it. Direction matters for the asymmetric types, so relinking
    /// the other way round returns the original rather than creating a contradictory second row.
    /// </remarks>
    Task<int> LinkAsync(
        int sourceClipId,
        int targetClipId,
        ClipLinkType linkType,
        string? note = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a link.</summary>
    /// <param name="linkId">The link to remove.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    Task UnlinkAsync(int linkId, CancellationToken cancellationToken = default);

    /// <summary>Counts the links on each of the given clips.</summary>
    /// <param name="clipIds">The clips to count for.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>Clip identifier to link count, omitting clips with none.</returns>
    Task<IReadOnlyDictionary<int, int>> CountByClipAsync(
        IEnumerable<int> clipIds, CancellationToken cancellationToken = default);
}
