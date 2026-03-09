using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClipStudio.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IHighlightRepository"/>.
/// </summary>
internal sealed class HighlightRepository : IHighlightRepository
{
    private readonly AppDbContext _context;

    /// <summary>Initializes a new instance of <see cref="HighlightRepository"/>.</summary>
    public HighlightRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<Highlight?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => await _context.Highlights
            .Include(h => h.HighlightTags).ThenInclude(ht => ht.Tag)
            .FirstOrDefaultAsync(h => h.Id == id, cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Highlight>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _context.Highlights
            .Where(h => h.Clip.Status != ClipStatus.Archived && !h.Clip.IsDeleted)
            .Include(h => h.Clip)
                .ThenInclude(c => c.ClipTags)
                    .ThenInclude(ct => ct.Tag)
            .Include(h => h.Clip)
                .ThenInclude(c => c.ClipPlayers)
                    .ThenInclude(cp => cp.Player)
            .Include(h => h.HighlightTags).ThenInclude(ht => ht.Tag)
            .OrderByDescending(h => h.CreatedAt)
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Highlight>> GetByClipAsync(int clipId, CancellationToken cancellationToken = default)
        => await _context.Highlights
            .Where(h => h.ClipId == clipId)
            .Include(h => h.HighlightTags).ThenInclude(ht => ht.Tag)
            .OrderBy(h => h.StartTime)
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task AddAsync(Highlight highlight, CancellationToken cancellationToken = default)
    {
        await _context.Highlights.AddAsync(highlight, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(Highlight highlight, CancellationToken cancellationToken = default)
    {
        _context.Highlights.Update(highlight);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var highlight = await _context.Highlights.FindAsync([id], cancellationToken);
        if (highlight is not null)
        {
            _context.Highlights.Remove(highlight);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
