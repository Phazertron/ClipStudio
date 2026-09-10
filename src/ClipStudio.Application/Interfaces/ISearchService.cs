using ClipStudio.Application.Models;

namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Answers one search term across everything the library holds.
/// </summary>
/// <remarks>
/// Distinct from the filter panel, which narrows the library to a set of clips. This answers
/// "where is the thing I am thinking of", which might be a clip, a highlight, a tag, a game, a
/// player, or something somebody said on camera.
/// </remarks>
public interface ISearchService
{
    /// <summary>
    /// Searches every kind of thing at once.
    /// </summary>
    /// <param name="term">What to look for.</param>
    /// <param name="includeCaptions">
    /// Whether to search transcripts too. Off by default: it is the most expensive of the queries
    /// and only useful once clips have been transcribed.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>
    /// One group per kind that had matches, each capped and carrying its full count. Empty for a
    /// term too short to be worth searching for.
    /// </returns>
    Task<IReadOnlyList<SearchResultGroup>> SearchAsync(
        string term, bool includeCaptions = false, CancellationToken cancellationToken = default);
}
