using ClipStudio.Application.Models;

namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Provides credential-free game search functionality backed by the Steam Community API.
/// </summary>
public interface IGameSearchService
{
    /// <summary>
    /// Searches for Steam games whose title matches the given query string.
    /// Returns an empty list on network errors or when no results are found.
    /// </summary>
    /// <param name="query">The search query string.</param>
    /// <param name="limit">Maximum number of results to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SteamGame>> SearchAsync(
        string query,
        int limit = 15,
        CancellationToken cancellationToken = default);
}
