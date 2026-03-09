using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClipStudio.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ISourceFolderRepository"/>.
/// </summary>
internal sealed class SourceFolderRepository : ISourceFolderRepository
{
    private readonly AppDbContext _context;

    /// <summary>Initializes a new instance of <see cref="SourceFolderRepository"/>.</summary>
    public SourceFolderRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SourceFolder>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _context.SourceFolders
            .AsNoTracking()
            .OrderBy(sf => sf.Path)
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SourceFolder>> GetActiveAsync(CancellationToken cancellationToken = default)
        => await _context.SourceFolders
            .AsNoTracking()
            .Where(sf => sf.IsActive)
            .OrderBy(sf => sf.Path)
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<SourceFolder?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => await _context.SourceFolders.FindAsync([id], cancellationToken);

    /// <inheritdoc/>
    public async Task AddAsync(SourceFolder folder, CancellationToken cancellationToken = default)
    {
        await _context.SourceFolders.AddAsync(folder, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(SourceFolder folder, CancellationToken cancellationToken = default)
    {
        _context.SourceFolders.Update(folder);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var folder = await _context.SourceFolders.FindAsync([id], cancellationToken);
        if (folder is not null)
        {
            _context.SourceFolders.Remove(folder);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
