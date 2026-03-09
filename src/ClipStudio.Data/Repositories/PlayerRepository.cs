using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClipStudio.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPlayerRepository"/> backed by <see cref="AppDbContext"/>.
/// </summary>
internal sealed class PlayerRepository : IPlayerRepository
{
    private readonly AppDbContext _db;

    /// <summary>Initializes a new instance of <see cref="PlayerRepository"/>.</summary>
    /// <param name="db">The database context.</param>
    public PlayerRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Player>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Players
            .Include(p => p.Aliases)
            .OrderBy(p => p.DisplayName)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Player?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _db.Players
            .Include(p => p.Aliases)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Player>> GetByClipAsync(int clipId, CancellationToken cancellationToken = default)
    {
        return await _db.ClipPlayers
            .Where(cp => cp.ClipId == clipId)
            .Include(cp => cp.Player)
                .ThenInclude(p => p.Aliases)
            .Select(cp => cp.Player)
            .OrderBy(p => p.DisplayName)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Player> AddAsync(Player player, CancellationToken cancellationToken = default)
    {
        _db.Players.Add(player);
        await _db.SaveChangesAsync(cancellationToken);
        return player;
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(Player player, CancellationToken cancellationToken = default)
    {
        _db.Players.Update(player);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var player = await _db.Players.FindAsync(new object[] { id }, cancellationToken);
        if (player is not null)
        {
            _db.Players.Remove(player);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task AddAliasAsync(PlayerAlias alias, CancellationToken cancellationToken = default)
    {
        _db.PlayerAliases.Add(alias);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task RemoveAliasAsync(int aliasId, CancellationToken cancellationToken = default)
    {
        var alias = await _db.PlayerAliases.FindAsync(new object[] { aliasId }, cancellationToken);
        if (alias is not null)
        {
            _db.PlayerAliases.Remove(alias);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Player>> GetMePlayersAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Players
            .Where(p => p.IsMe)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task ClearIsMeExceptAsync(int exceptPlayerId, CancellationToken cancellationToken = default)
    {
        var others = await _db.Players
            .Where(p => p.IsMe && p.Id != exceptPlayerId)
            .ToListAsync(cancellationToken);

        foreach (var p in others)
            p.IsMe = false;

        if (others.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task TagClipAsync(int clipId, int playerId, CancellationToken cancellationToken = default)
    {
        var exists = await _db.ClipPlayers
            .AnyAsync(cp => cp.ClipId == clipId && cp.PlayerId == playerId, cancellationToken);

        if (!exists)
        {
            _db.ClipPlayers.Add(new ClipPlayer { ClipId = clipId, PlayerId = playerId });
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task UntagClipAsync(int clipId, int playerId, CancellationToken cancellationToken = default)
    {
        var record = await _db.ClipPlayers
            .FirstOrDefaultAsync(cp => cp.ClipId == clipId && cp.PlayerId == playerId, cancellationToken);

        if (record is not null)
        {
            _db.ClipPlayers.Remove(record);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
