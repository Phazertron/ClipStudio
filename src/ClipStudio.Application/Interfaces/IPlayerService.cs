using ClipStudio.Core.Entities;

namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Application service for managing <see cref="Player"/> entities.
/// </summary>
public interface IPlayerService
{
    /// <summary>Returns all players ordered by display name.</summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task<IReadOnlyList<Player>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all players tagged on the specified clip.</summary>
    /// <param name="clipId">The clip identifier.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task<IReadOnlyList<Player>> GetByClipAsync(int clipId, CancellationToken cancellationToken = default);

    /// <summary>Creates a new player and returns it with its assigned identifier.</summary>
    /// <param name="displayName">The primary display name.</param>
    /// <param name="isMe">Whether this player represents the local user.</param>
    /// <param name="iconPath">Optional absolute path to an icon image.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task<Player> CreateAsync(string displayName, bool isMe, string? iconPath, CancellationToken cancellationToken = default);

    /// <summary>Updates a player's core properties.</summary>
    /// <param name="id">The player identifier.</param>
    /// <param name="displayName">The new primary display name.</param>
    /// <param name="isMe">Whether this player represents the local user.</param>
    /// <param name="iconPath">Optional absolute path to an icon image.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task UpdateAsync(int id, string displayName, bool isMe, string? iconPath, CancellationToken cancellationToken = default);

    /// <summary>Deletes a player and removes all their clip associations.</summary>
    /// <param name="id">The player identifier.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Adds an alias to the specified player.</summary>
    /// <param name="playerId">The player identifier.</param>
    /// <param name="alias">The alias text to add.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task<PlayerAlias> AddAliasAsync(int playerId, string alias, CancellationToken cancellationToken = default);

    /// <summary>Removes the alias with the given identifier.</summary>
    /// <param name="aliasId">The alias identifier.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task RemoveAliasAsync(int aliasId, CancellationToken cancellationToken = default);

    /// <summary>Tags the specified clip with the specified player.</summary>
    /// <param name="clipId">The clip identifier.</param>
    /// <param name="playerId">The player identifier.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task TagClipAsync(int clipId, int playerId, CancellationToken cancellationToken = default);

    /// <summary>Removes the player tag from the specified clip.</summary>
    /// <param name="clipId">The clip identifier.</param>
    /// <param name="playerId">The player identifier.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task UntagClipAsync(int clipId, int playerId, CancellationToken cancellationToken = default);

    /// <summary>Removes all player associations from the specified clip.</summary>
    /// <param name="clipId">The clip identifier.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task UntagAllAsync(int clipId, CancellationToken cancellationToken = default);
}
