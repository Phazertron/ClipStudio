using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;

namespace ClipStudio.Application.Services;

/// <summary>
/// Suggests tags for a clip using a composite scoring algorithm that weighs
/// game affinity (tags commonly used with the same game), player affinity
/// (tags commonly used with the same players), and co-occurrence
/// (tags frequently paired with the clip's existing tags).
/// </summary>
public sealed class TagSuggestionService : ITagSuggestionService
{
    private readonly IClipRepository _clips;

    /// <summary>Initializes a new instance of <see cref="TagSuggestionService"/>.</summary>
    public TagSuggestionService(IClipRepository clips)
    {
        _clips = clips;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TagSuggestion>> GetSuggestionsAsync(
        int clipId,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        var allClips = await _clips.GetAllAsync(cancellationToken);

        // GetAllAsync includes ClipTags+Tag and ClipPlayers+Player but not HighlightTags.
        var target = allClips.FirstOrDefault(c => c.Id == clipId);
        if (target is null)
            return Array.Empty<TagSuggestion>();


        // Tags already on this clip (directly) — excluded from suggestions.
        // GetAllAsync does not include HighlightTags so only clip-level tags are checked here.
        var existingTagIds = new HashSet<int>(target.ClipTags.Select(ct => ct.TagId));

        // Game tags on the target clip (to find clips with the same game).
        var targetGameTagIds = target.ClipTags
            .Where(ct => ct.Tag?.Type == TagType.Game)
            .Select(ct => ct.TagId)
            .ToHashSet();

        // Player IDs on the target clip.
        var targetPlayerIds = target.ClipPlayers
            .Select(cp => cp.PlayerId)
            .ToHashSet();

        var scores = new Dictionary<int, (int Score, string Reason, ClipStudio.Core.Entities.Tag Tag)>();

        foreach (var clip in allClips)
        {
            if (clip.Id == clipId)
                continue;

            // Determine which affinity signals apply.
            var hasGameMatch   = clip.ClipTags.Any(ct => targetGameTagIds.Contains(ct.TagId));
            var hasPlayerMatch = clip.ClipPlayers.Any(cp => targetPlayerIds.Contains(cp.PlayerId));

            // Only consider clips that share at least one signal.
            if (!hasGameMatch && !hasPlayerMatch && targetGameTagIds.Count == 0 && targetPlayerIds.Count == 0)
            {
                // No signal context; fall back to pure co-occurrence with existing tags.
                hasGameMatch = false;
            }

            var clipTagIds = clip.ClipTags.Select(ct => ct.TagId).ToHashSet();
            var hasCoOccurrence = clipTagIds.Overlaps(existingTagIds);

            if (!hasGameMatch && !hasPlayerMatch && !hasCoOccurrence)
                continue;

            // Build per-tag scores for every General tag on the candidate clip.
            foreach (var ct in clip.ClipTags)
            {
                if (ct.Tag is null || ct.Tag.Type != TagType.General)
                    continue;
                if (existingTagIds.Contains(ct.TagId))
                    continue;

                var delta = 0;
                var reasons = new List<string>();

                if (hasGameMatch)
                {
                    delta += 3;
                    reasons.Add("same game");
                }

                if (hasPlayerMatch)
                {
                    delta += 2;
                    reasons.Add("same players");
                }

                if (hasCoOccurrence)
                {
                    delta += 1;
                    reasons.Add("co-occurrence");
                }

                if (!scores.TryGetValue(ct.TagId, out var existing))
                {
                    scores[ct.TagId] = (delta, BuildReason(reasons), ct.Tag);
                }
                else
                {
                    scores[ct.TagId] = (existing.Score + delta, existing.Reason, existing.Tag);
                }
            }
        }

        return scores
            .OrderByDescending(kvp => kvp.Value.Score)
            .Take(maxResults)
            .Select(kvp => new TagSuggestion(kvp.Value.Tag, kvp.Value.Reason, kvp.Value.Score))
            .ToList();
    }

    private static string BuildReason(IReadOnlyList<string> parts)
    {
        return parts.Count switch
        {
            0 => "frequent",
            1 => parts[0],
            2 => $"{parts[0]} + {parts[1]}",
            _ => string.Join(", ", parts)
        };
    }
}
