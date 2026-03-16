using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClipStudio.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IFilterPresetRepository"/>.
/// </summary>
internal sealed class FilterPresetRepository : IFilterPresetRepository
{
    private readonly AppDbContext _context;

    /// <summary>Initializes a new instance of <see cref="FilterPresetRepository"/>.</summary>
    public FilterPresetRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FilterPreset>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _context.FilterPresets
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<FilterPreset?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => await _context.FilterPresets
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    /// <inheritdoc/>
    public async Task AddAsync(FilterPreset preset, CancellationToken cancellationToken = default)
    {
        _context.FilterPresets.Add(preset);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var preset = await _context.FilterPresets.FindAsync([id], cancellationToken);
        if (preset is not null)
        {
            _context.FilterPresets.Remove(preset);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
