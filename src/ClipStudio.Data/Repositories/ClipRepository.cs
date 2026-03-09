using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClipStudio.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IClipRepository"/>.
/// </summary>
internal sealed class ClipRepository : IClipRepository
{
    private readonly AppDbContext _context;

    /// <summary>Initializes a new instance of <see cref="ClipRepository"/>.</summary>
    public ClipRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<Clip?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => await _context.Clips
            .AsNoTrackingWithIdentityResolution()
            .Include(c => c.ClipTags).ThenInclude(ct => ct.Tag)
            .Include(c => c.Highlights).ThenInclude(h => h.HighlightTags).ThenInclude(ht => ht.Tag)
            .Include(c => c.Screenshots)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Clip>> GetByStatusAsync(ClipStatus status, CancellationToken cancellationToken = default)
        => await _context.Clips
            .Where(c => !c.IsDeleted && c.Status == status)
            .AsNoTrackingWithIdentityResolution()
            .Include(c => c.ClipTags).ThenInclude(ct => ct.Tag)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Clip>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _context.Clips
            .Where(c => !c.IsDeleted)
            .AsNoTrackingWithIdentityResolution()
            .Include(c => c.ClipTags).ThenInclude(ct => ct.Tag)
            .Include(c => c.ClipPlayers).ThenInclude(cp => cp.Player)
            .Include(c => c.Highlights)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Clip>> GetByTagsAsync(
        IEnumerable<int> tagIds,
        CancellationToken cancellationToken = default)
    {
        var tagIdList = tagIds.ToList();

        // A clip matches if it has a direct clip-level tag OR any highlight with a matching tag.
        return await _context.Clips
            .Where(c => !c.IsDeleted &&
                (c.ClipTags.Any(ct => tagIdList.Contains(ct.TagId)) ||
                 c.Highlights.Any(h => h.HighlightTags.Any(ht => tagIdList.Contains(ht.TagId)))))
            .AsNoTrackingWithIdentityResolution()
            .Include(c => c.ClipTags).ThenInclude(ct => ct.Tag)
            .Include(c => c.ClipPlayers).ThenInclude(cp => cp.Player)
            .Include(c => c.Highlights)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Clip>> GetTrashedAsync(CancellationToken cancellationToken = default)
        => await _context.Clips
            .Where(c => c.IsDeleted)
            .AsNoTrackingWithIdentityResolution()
            .OrderByDescending(c => c.DeletedAt)
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Clip>> GetBySourceFolderIdAsync(int folderId, CancellationToken cancellationToken = default)
        => await _context.Clips
            .Where(c => c.SourceFolderId == folderId)
            .AsNoTrackingWithIdentityResolution()
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task AddAsync(Clip clip, CancellationToken cancellationToken = default)
    {
        await _context.Clips.AddAsync(clip, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(Clip clip, CancellationToken cancellationToken = default)
    {
        // Clear any previously tracked entities before attaching the disconnected graph.
        // This prevents InvalidOperationException when the same entity (e.g., a Tag that appears
        // in both ClipTags and HighlightTags) was loaded with AsNoTrackingWithIdentityResolution
        // and is now being re-attached via Update.
        _context.ChangeTracker.Clear();
        _context.Clips.Update(clip);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task AddClipTagAsync(int clipId, int tagId, CancellationToken cancellationToken = default)
    {
        var exists = await _context.Set<ClipTag>()
            .AnyAsync(ct => ct.ClipId == clipId && ct.TagId == tagId, cancellationToken);
        if (exists) return;

        _context.Set<ClipTag>().Add(new ClipTag { ClipId = clipId, TagId = tagId });
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task RemoveClipTagAsync(int clipId, int tagId, CancellationToken cancellationToken = default)
    {
        var assignment = await _context.Set<ClipTag>()
            .FirstOrDefaultAsync(ct => ct.ClipId == clipId && ct.TagId == tagId, cancellationToken);
        if (assignment is null) return;

        _context.Set<ClipTag>().Remove(assignment);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var clip = await _context.Clips.FindAsync([id], cancellationToken);
        if (clip is not null)
        {
            _context.Clips.Remove(clip);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsByFilePathAsync(string filePath, CancellationToken cancellationToken = default)
        => await _context.Clips.AnyAsync(c => c.FilePath == filePath, cancellationToken);
}
