using ClipStudio.Core.Entities;

namespace ClipStudio.Core.Interfaces;

/// <summary>
/// Persistence operations for <see cref="GameTagAlias"/> entities.
/// </summary>
public interface IGameTagAliasRepository
{
    /// <summary>
    /// Returns all game tag aliases, including their associated <see cref="Tag"/>.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task<IReadOnlyList<GameTagAlias>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the first alias whose <see cref="GameTagAlias.AliasString"/> matches
    /// <paramref name="aliasString"/> using a case-insensitive comparison.
    /// Returns null if no match is found.
    /// </summary>
    /// <param name="aliasString">The OBS-derived game name string to look up.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task<GameTagAlias?> FindByAliasAsync(string aliasString, CancellationToken cancellationToken = default);

    /// <summary>Persists a new alias and returns it with its database-assigned identifier.</summary>
    /// <param name="alias">The alias entity to add.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task<GameTagAlias> AddAsync(GameTagAlias alias, CancellationToken cancellationToken = default);

    /// <summary>Deletes the alias with the given identifier.</summary>
    /// <param name="id">The alias identifier.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
