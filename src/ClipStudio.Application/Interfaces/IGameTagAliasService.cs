using ClipStudio.Core.Entities;

namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Application service for managing game-tag alias mappings.
/// Aliases allow a raw OBS-derived game name string to be automatically mapped to a confirmed
/// Game-type <see cref="Tag"/> during import, without requiring user interaction.
/// </summary>
public interface IGameTagAliasService
{
    /// <summary>
    /// Looks up a <see cref="GameTagAlias"/> by the raw OBS-derived alias string.
    /// Returns null if no matching alias exists.
    /// </summary>
    /// <param name="aliasString">The OBS-parsed game name to look up (case-insensitive).</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task<GameTagAlias?> FindByAliasAsync(string aliasString, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new alias mapping from <paramref name="aliasString"/> to the game tag with
    /// <paramref name="tagId"/>, unless an identical alias already exists.
    /// </summary>
    /// <param name="aliasString">The OBS-parsed game name string to remember.</param>
    /// <param name="tagId">The identifier of the Game-type tag to map to.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The created or existing <see cref="GameTagAlias"/>.</returns>
    Task<GameTagAlias> EnsureAliasAsync(string aliasString, int tagId, CancellationToken cancellationToken = default);
}
