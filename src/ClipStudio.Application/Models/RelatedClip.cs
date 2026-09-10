using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;

namespace ClipStudio.Application.Models;

/// <summary>
/// One clip related to another, seen from the asking clip's side.
/// </summary>
/// <param name="LinkId">The link's identifier, so it can be removed.</param>
/// <param name="Clip">The clip at the other end.</param>
/// <param name="LinkType">The kind of relationship.</param>
/// <param name="RelationshipLabel">
/// How the relationship reads from the asking clip's side, which is not always how it was stored.
/// </param>
/// <param name="Note">The note the user attached to the link, if any.</param>
/// <remarks>
/// The label is resolved here rather than in the view because it depends on which end you are
/// looking from: the clip you linked as a Sequel shows as "Sequel" on the clip you linked it from,
/// and that clip shows as "Prequel" on the sequel. Leaving that to the UI would mean every place
/// that shows a link has to know the rule.
/// </remarks>
public sealed record RelatedClip(
    int LinkId,
    Clip Clip,
    ClipLinkType LinkType,
    string RelationshipLabel,
    string? Note);
