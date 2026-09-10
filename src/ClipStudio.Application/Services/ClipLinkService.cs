using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ClipStudio.Application.Services;

/// <summary>
/// Creates and reads the relationships the user draws between clips.
/// </summary>
public sealed class ClipLinkService : IClipLinkService
{
    private readonly IClipLinkRepository _links;
    private readonly ILogger<ClipLinkService> _logger;

    /// <summary>Initialises a new <see cref="ClipLinkService"/>.</summary>
    /// <param name="links">The link repository.</param>
    /// <param name="logger">The logger.</param>
    public ClipLinkService(IClipLinkRepository links, ILogger<ClipLinkService> logger)
    {
        _links  = links;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RelatedClip>> GetRelatedAsync(
        int clipId, CancellationToken cancellationToken = default)
    {
        var links = await _links.GetForClipAsync(clipId, cancellationToken);
        var related = new List<RelatedClip>(links.Count);

        foreach (var link in links)
        {
            // Which end of the link the asking clip is on decides both which clip to show and how
            // the relationship reads.
            var isSource = link.SourceClipId == clipId;
            var other    = isSource ? link.TargetClip : link.SourceClip;

            if (other is null) continue;

            related.Add(new RelatedClip(
                link.Id,
                other,
                link.LinkType,
                DescribeFrom(link.LinkType, isSource),
                link.Note));
        }

        return related;
    }

    /// <inheritdoc/>
    public async Task<int> LinkAsync(
        int sourceClipId,
        int targetClipId,
        ClipLinkType linkType,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        if (sourceClipId == targetClipId)
            throw new ArgumentException("A clip cannot be linked to itself.", nameof(targetClipId));

        // Checked in both directions: the unique index only covers the stored direction, so
        // linking B to A after linking A to B would otherwise create a second row describing the
        // same relationship - and for a Sequel, a contradictory one.
        var existing = await _links.FindAsync(sourceClipId, targetClipId, linkType, cancellationToken);
        if (existing is not null)
        {
            _logger.LogDebug(
                "Clips {Source} and {Target} are already linked as {LinkType}.",
                sourceClipId, targetClipId, linkType);
            return existing.Id;
        }

        var link = new ClipLink
        {
            SourceClipId = sourceClipId,
            TargetClipId = targetClipId,
            LinkType     = linkType,
            Note         = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedAt    = DateTime.UtcNow,
        };

        await _links.AddAsync(link, cancellationToken);
        _logger.LogInformation(
            "Linked clip {Source} to {Target} as {LinkType}.", sourceClipId, targetClipId, linkType);

        return link.Id;
    }

    /// <inheritdoc/>
    public Task UnlinkAsync(int linkId, CancellationToken cancellationToken = default)
        => _links.DeleteAsync(linkId, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyDictionary<int, int>> CountByClipAsync(
        IEnumerable<int> clipIds, CancellationToken cancellationToken = default)
        => _links.CountByClipAsync(clipIds, cancellationToken);

    /// <summary>
    /// Describes a relationship from one end of it.
    /// </summary>
    /// <param name="linkType">The kind of relationship.</param>
    /// <param name="fromSource">Whether the description is for the clip the link was created from.</param>
    /// <returns>The label to show.</returns>
    /// <remarks>
    /// Two of the types are symmetric and read the same either way. The other two have a direction,
    /// and the clip on the far end gets the opposite wording: the clip you marked as a sequel shows
    /// its origin as a prequel, not as another sequel.
    /// </remarks>
    public static string DescribeFrom(ClipLinkType linkType, bool fromSource) => linkType switch
    {
        ClipLinkType.SameMoment => "Same moment",
        ClipLinkType.Variant    => "Variant",
        ClipLinkType.Sequel     => fromSource ? "Sequel" : "Prequel",
        ClipLinkType.Reaction   => fromSource ? "Reaction" : "Reacted to",
        _                       => linkType.ToString(),
    };

    /// <summary>Describes a relationship as it is offered when creating a link.</summary>
    /// <param name="linkType">The kind of relationship.</param>
    /// <returns>The label to show in the picker.</returns>
    public static string Describe(ClipLinkType linkType) => DescribeFrom(linkType, fromSource: true);
}
