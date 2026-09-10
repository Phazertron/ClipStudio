using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClipStudio.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IClipLinkRepository"/>.
/// </summary>
internal sealed class ClipLinkRepository : IClipLinkRepository
{
    private readonly AppDbContext _context;

    /// <summary>Initialises a new <see cref="ClipLinkRepository"/>.</summary>
    /// <param name="context">The database context.</param>
    public ClipLinkRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ClipLink>> GetForClipAsync(
        int clipId, CancellationToken cancellationToken = default)
        => await _context.ClipLinks
            .AsNoTrackingWithIdentityResolution()
            .Include(l => l.SourceClip).ThenInclude(c => c.ClipTags).ThenInclude(ct => ct.Tag)
            .Include(l => l.TargetClip).ThenInclude(c => c.ClipTags).ThenInclude(ct => ct.Tag)
            .Where(l => l.SourceClipId == clipId || l.TargetClipId == clipId)
            // A trashed clip is not gone, but it should not appear as a related clip while it is
            // in the trash. The link itself survives, so restoring brings the relationship back.
            .Where(l => !l.SourceClip.IsDeleted && !l.TargetClip.IsDeleted)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<ClipLink?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => await _context.ClipLinks
            .AsNoTrackingWithIdentityResolution()
            .Include(l => l.SourceClip)
            .Include(l => l.TargetClip)
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    /// <inheritdoc/>
    public async Task<ClipLink?> FindAsync(
        int clipId, int otherClipId, ClipLinkType linkType, CancellationToken cancellationToken = default)
        => await _context.ClipLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(
                l => l.LinkType == linkType
                     && ((l.SourceClipId == clipId && l.TargetClipId == otherClipId)
                         || (l.SourceClipId == otherClipId && l.TargetClipId == clipId)),
                cancellationToken);

    /// <inheritdoc/>
    public async Task AddAsync(ClipLink link, CancellationToken cancellationToken = default)
    {
        await _context.ClipLinks.AddAsync(link, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var link = await _context.ClipLinks.FindAsync([id], cancellationToken);
        if (link is null) return;

        _context.ClipLinks.Remove(link);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<int, int>> CountByClipAsync(
        IEnumerable<int> clipIds, CancellationToken cancellationToken = default)
    {
        var ids = clipIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, int>();

        var rows = await _context.ClipLinks
            .AsNoTracking()
            .Where(l => !l.SourceClip.IsDeleted && !l.TargetClip.IsDeleted)
            .Where(l => ids.Contains(l.SourceClipId) || ids.Contains(l.TargetClipId))
            .Select(l => new { l.SourceClipId, l.TargetClipId })
            .ToListAsync(cancellationToken);

        var counts = new Dictionary<int, int>();
        foreach (var row in rows)
        {
            if (ids.Contains(row.SourceClipId))
                counts[row.SourceClipId] = counts.GetValueOrDefault(row.SourceClipId) + 1;

            if (ids.Contains(row.TargetClipId))
                counts[row.TargetClipId] = counts.GetValueOrDefault(row.TargetClipId) + 1;
        }

        return counts;
    }
}
