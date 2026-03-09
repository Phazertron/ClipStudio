using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClipStudio.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ITagRepository"/>.
/// </summary>
internal sealed class TagRepository : ITagRepository
{
    private readonly AppDbContext _context;

    /// <summary>Initializes a new instance of <see cref="TagRepository"/>.</summary>
    public TagRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<Tag?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => await _context.Tags
            .Include(t => t.ChildTags)
            .Include(t => t.Relations).ThenInclude(r => r.RelatedTag)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Tag>> GetByTypeAsync(TagType type, CancellationToken cancellationToken = default)
        => await _context.Tags
            .Where(t => t.Type == type)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Tag>> GetRootsAsync(CancellationToken cancellationToken = default)
        => await _context.Tags
            .Where(t => t.ParentTagId == null)
            .Include(t => t.ChildTags)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Tag>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _context.Tags
            .Include(t => t.ClipTags)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<int>> GetDescendantIdsAsync(int tagId, CancellationToken cancellationToken = default)
    {
        // Load the full tag list into memory and traverse the hierarchy.
        // Tag libraries are small; this avoids raw SQL recursive CTEs while remaining correct.
        var allTags = await _context.Tags
            .Select(t => new { t.Id, t.ParentTagId })
            .ToListAsync(cancellationToken);

        var result = new List<int>();
        var queue = new Queue<int>();
        queue.Enqueue(tagId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var child in allTags.Where(t => t.ParentTagId == current))
            {
                result.Add(child.Id);
                queue.Enqueue(child.Id);
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task AddAsync(Tag tag, CancellationToken cancellationToken = default)
    {
        await _context.Tags.AddAsync(tag, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(Tag tag, CancellationToken cancellationToken = default)
    {
        _context.Tags.Update(tag);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var tag = await _context.Tags.FindAsync([id], cancellationToken);
        if (tag is not null)
        {
            _context.Tags.Remove(tag);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task AddRelationAsync(int tagId, int relatedTagId, CancellationToken cancellationToken = default)
    {
        // Relations are stored in both directions to simplify querying.
        var forward = new TagRelation { TagId = tagId, RelatedTagId = relatedTagId };
        var reverse = new TagRelation { TagId = relatedTagId, RelatedTagId = tagId };

        var forwardExists = await _context.TagRelations
            .AnyAsync(tr => tr.TagId == tagId && tr.RelatedTagId == relatedTagId, cancellationToken);

        if (!forwardExists)
        {
            await _context.TagRelations.AddRangeAsync([forward, reverse], cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task RemoveRelationAsync(int tagId, int relatedTagId, CancellationToken cancellationToken = default)
    {
        var relations = await _context.TagRelations
            .Where(tr =>
                (tr.TagId == tagId && tr.RelatedTagId == relatedTagId) ||
                (tr.TagId == relatedTagId && tr.RelatedTagId == tagId))
            .ToListAsync(cancellationToken);

        if (relations.Count > 0)
        {
            _context.TagRelations.RemoveRange(relations);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
