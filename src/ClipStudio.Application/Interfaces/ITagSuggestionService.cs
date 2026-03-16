using ClipStudio.Application.Models;

namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Suggests tags to apply to a clip based on frequency, co-occurrence, and game affinity.
/// </summary>
public interface ITagSuggestionService
{
    /// <summary>
    /// Returns up to <paramref name="maxResults"/> tag suggestions for the given clip.
    /// Suggestions are ranked by a composite score that weighs game affinity, player affinity,
    /// and co-occurrence with tags already applied to the clip.
    /// Tags already applied to the clip (directly or via highlights) are excluded.
    /// </summary>
    /// <param name="clipId">The identifier of the clip to suggest tags for.</param>
    /// <param name="maxResults">Maximum number of suggestions to return. Defaults to 5.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task<IReadOnlyList<TagSuggestion>> GetSuggestionsAsync(
        int clipId,
        int maxResults = 5,
        CancellationToken cancellationToken = default);
}
