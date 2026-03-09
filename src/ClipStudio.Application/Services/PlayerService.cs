using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ClipStudio.Application.Services;

/// <summary>
/// Application service for managing <see cref="Player"/> entities.
/// Enforces the single-IsMe constraint: when a player is created or updated with
/// <c>IsMe = true</c>, all other players have their <c>IsMe</c> flag cleared.
/// </summary>
public sealed class PlayerService : IPlayerService
{
    private readonly IPlayerRepository _players;
    private readonly ILogger<PlayerService> _logger;

    /// <summary>Initializes a new instance of <see cref="PlayerService"/>.</summary>
    /// <param name="players">The player repository.</param>
    /// <param name="logger">The logger instance.</param>
    public PlayerService(IPlayerRepository players, ILogger<PlayerService> logger)
    {
        _players = players;
        _logger  = logger;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Player>> GetAllAsync(CancellationToken cancellationToken = default)
        => _players.GetAllAsync(cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<Player>> GetByClipAsync(int clipId, CancellationToken cancellationToken = default)
        => _players.GetByClipAsync(clipId, cancellationToken);

    /// <inheritdoc/>
    public async Task<Player> CreateAsync(
        string displayName,
        bool isMe,
        string? iconPath,
        CancellationToken cancellationToken = default)
    {
        var player = new Player
        {
            DisplayName = displayName.Trim(),
            IsMe        = isMe,
            IconPath    = string.IsNullOrWhiteSpace(iconPath) ? null : iconPath.Trim(),
            CreatedAt   = DateTime.UtcNow
        };

        await _players.AddAsync(player, cancellationToken);

        if (isMe)
            await _players.ClearIsMeExceptAsync(player.Id, cancellationToken);

        _logger.LogInformation("Player '{Name}' created (Id={Id}, IsMe={IsMe}).", displayName, player.Id, isMe);
        return player;
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(
        int id,
        string displayName,
        bool isMe,
        string? iconPath,
        CancellationToken cancellationToken = default)
    {
        var player = await _players.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Player {id} not found.");

        player.DisplayName = displayName.Trim();
        player.IsMe        = isMe;
        player.IconPath    = string.IsNullOrWhiteSpace(iconPath) ? null : iconPath.Trim();

        await _players.UpdateAsync(player, cancellationToken);

        if (isMe)
            await _players.ClearIsMeExceptAsync(id, cancellationToken);

        _logger.LogInformation("Player {Id} updated.", id);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Player {Id} deleted.", id);
        return _players.DeleteAsync(id, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<PlayerAlias> AddAliasAsync(int playerId, string alias, CancellationToken cancellationToken = default)
    {
        var aliasEntity = new PlayerAlias { PlayerId = playerId, Alias = alias.Trim() };
        await _players.AddAliasAsync(aliasEntity, cancellationToken);
        _logger.LogInformation("Alias '{Alias}' added to player {PlayerId}.", alias, playerId);
        return aliasEntity;
    }

    /// <inheritdoc/>
    public Task RemoveAliasAsync(int aliasId, CancellationToken cancellationToken = default)
        => _players.RemoveAliasAsync(aliasId, cancellationToken);

    /// <inheritdoc/>
    public Task TagClipAsync(int clipId, int playerId, CancellationToken cancellationToken = default)
        => _players.TagClipAsync(clipId, playerId, cancellationToken);

    /// <inheritdoc/>
    public Task UntagClipAsync(int clipId, int playerId, CancellationToken cancellationToken = default)
        => _players.UntagClipAsync(clipId, playerId, cancellationToken);

    /// <inheritdoc/>
    public async Task UntagAllAsync(int clipId, CancellationToken cancellationToken = default)
    {
        var players = await GetByClipAsync(clipId, cancellationToken);
        foreach (var player in players)
            await _players.UntagClipAsync(clipId, player.Id, cancellationToken);
    }
}
