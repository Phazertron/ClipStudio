using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClipStudio.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IScreenshotRepository"/>.
/// </summary>
internal sealed class ScreenshotRepository : IScreenshotRepository
{
    private readonly AppDbContext _context;

    /// <summary>Initializes a new instance of <see cref="ScreenshotRepository"/>.</summary>
    public ScreenshotRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<Screenshot?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => await _context.Screenshots.FindAsync([id], cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Screenshot>> GetByClipAsync(int clipId, CancellationToken cancellationToken = default)
        => await _context.Screenshots
            .Where(s => s.ClipId == clipId)
            .OrderBy(s => s.PlaybackTimestamp)
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task AddAsync(Screenshot screenshot, CancellationToken cancellationToken = default)
    {
        await _context.Screenshots.AddAsync(screenshot, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(Screenshot screenshot, CancellationToken cancellationToken = default)
    {
        _context.Screenshots.Update(screenshot);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var screenshot = await _context.Screenshots.FindAsync([id], cancellationToken);
        if (screenshot is not null)
        {
            _context.Screenshots.Remove(screenshot);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
