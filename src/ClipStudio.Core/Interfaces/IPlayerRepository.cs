using ClipStudio.Core.Entities;

namespace ClipStudio.Core.Interfaces;

/// <summary>
/// Persistence operations for <see cref="Player"/> entities and their related data.
/// </summary>
public interface IPlayerRepository
{
    /// <summary>Returns all players, including their aliases, ordered by display name.</summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task<IReadOnlyList<Player>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the player with the given identifier, including aliases. Returns null if not found.</summary>
    /// <param name="id">The player identifier.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task<Player?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Returns all players tagged on the given clip, including their aliases.</summary>
    /// <param name="clipId">The clip identifier.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task<IReadOnlyList<Player>> GetByClipAsync(int clipId, CancellationToken cancellationToken = default);

    /// <summary>Persists a new player and returns it with its database-assigned identifier.</summary>
    /// <param name="player">The player entity to add.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task<Player> AddAsync(Player player, CancellationToken cancellationToken = default);

    /// <summary>Persists changes to an existing player.</summary>
    /// <param name="player">The player entity to update.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task UpdateAsync(Player player, CancellationToken cancellationToken = default);

    /// <summary>Deletes the player with the given identifier.</summary>
    /// <param name="id">The player identifier.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Adds an alias to the specified player.</summary>
    /// <param name="alias">The alias entity to persist.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task AddAliasAsync(PlayerAlias alias, CancellationToken cancellationToken = default);

    /// <summary>Removes the alias with the given identifier.</summary>
    /// <param name="aliasId">The alias identifier.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task RemoveAliasAsync(int aliasId, CancellationToken cancellationToken = default);

    /// <summary>Returns all players marked as <c>IsMe = true</c>.</summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task<IReadOnlyList<Player>> GetMePlayersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the <c>IsMe</c> flag on all players except the one with the given identifier.
    /// Used to enforce the single-IsMe constraint.
    /// </summary>
    /// <param name="exceptPlayerId">The player whose <c>IsMe</c> flag should remain set.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task ClearIsMeExceptAsync(int exceptPlayerId, CancellationToken cancellationToken = default);

    /// <summary>Tags the specified clip with the specified player (adds a <see cref="ClipPlayer"/> row).</summary>
    /// <param name="clipId">The clip identifier.</param>
    /// <param name="playerId">The player identifier.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task TagClipAsync(int clipId, int playerId, CancellationToken cancellationToken = default);

    /// <summary>Removes the player tag from the specified clip.</summary>
    /// <param name="clipId">The clip identifier.</param>
    /// <param name="playerId">The player identifier.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task UntagClipAsync(int clipId, int playerId, CancellationToken cancellationToken = default);
}
