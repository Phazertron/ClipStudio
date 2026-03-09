using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClipStudio.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IExportJobRepository"/> backed by <see cref="AppDbContext"/>.
/// </summary>
internal sealed class ExportJobRepository : IExportJobRepository
{
    private readonly AppDbContext _db;

    /// <summary>Initializes a new instance of <see cref="ExportJobRepository"/>.</summary>
    /// <param name="db">The database context.</param>
    public ExportJobRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public async Task<ExportJob> AddAsync(ExportJob job, CancellationToken cancellationToken = default)
    {
        _db.ExportJobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);
        return job;
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(ExportJob job, CancellationToken cancellationToken = default)
    {
        _db.ExportJobs.Update(job);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ExportJob>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _db.ExportJobs
            .Include(j => j.Clip)
            .Include(j => j.Highlight)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ExportJob>> GetByClipAsync(int clipId, CancellationToken cancellationToken = default)
    {
        return await _db.ExportJobs
            .Where(j => j.ClipId == clipId)
            .Include(j => j.Clip)
            .Include(j => j.Highlight)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ExportJob>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        return await _db.ExportJobs
            .Where(j => j.Status == ExportJobStatus.Pending)
            .Include(j => j.Clip)
            .Include(j => j.Highlight)
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ExportJob?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _db.ExportJobs
            .Include(j => j.Clip)
            .Include(j => j.Highlight)
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
    }
}
