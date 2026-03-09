using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClipStudio.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IGameTagAliasRepository"/>.
/// </summary>
internal sealed class GameTagAliasRepository : IGameTagAliasRepository
{
    private readonly AppDbContext _context;

    /// <summary>Initializes a new instance of <see cref="GameTagAliasRepository"/>.</summary>
    public GameTagAliasRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<GameTagAlias>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _context.GameTagAliases
            .Include(a => a.Tag)
            .OrderBy(a => a.AliasString)
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<GameTagAlias?> FindByAliasAsync(string aliasString, CancellationToken cancellationToken = default)
        => await _context.GameTagAliases
            .Include(a => a.Tag)
            .FirstOrDefaultAsync(
                a => a.AliasString.ToLower() == aliasString.ToLower(),
                cancellationToken);

    /// <inheritdoc/>
    public async Task<GameTagAlias> AddAsync(GameTagAlias alias, CancellationToken cancellationToken = default)
    {
        _context.GameTagAliases.Add(alias);
        await _context.SaveChangesAsync(cancellationToken);
        return alias;
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var alias = await _context.GameTagAliases.FindAsync([id], cancellationToken);
        if (alias is not null)
        {
            _context.GameTagAliases.Remove(alias);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
