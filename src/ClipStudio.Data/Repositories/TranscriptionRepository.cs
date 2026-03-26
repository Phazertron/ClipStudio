using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClipStudio.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ITranscriptionRepository"/>.
/// </summary>
internal sealed class TranscriptionRepository : ITranscriptionRepository
{
    private readonly AppDbContext _context;

    /// <summary>Initializes a new instance of <see cref="TranscriptionRepository"/>.</summary>
    public TranscriptionRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Transcription>> GetByClipIdAsync(int clipId, CancellationToken cancellationToken = default)
        => await _context.Transcriptions
            .Where(t => t.ClipId == clipId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<Transcription?> GetLatestByClipIdAsync(int clipId, CancellationToken cancellationToken = default)
        => await _context.Transcriptions
            .Include(t => t.Segments.OrderBy(s => s.IndexNumber))
            .Where(t => t.ClipId == clipId)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TranscriptionSegment>> GetSegmentsAsync(int transcriptionId, CancellationToken cancellationToken = default)
        => await _context.TranscriptionSegments
            .Where(s => s.TranscriptionId == transcriptionId)
            .OrderBy(s => s.IndexNumber)
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task AddAsync(Transcription transcription, CancellationToken cancellationToken = default)
    {
        await _context.Transcriptions.AddAsync(transcription, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(int transcriptionId, CancellationToken cancellationToken = default)
    {
        var transcription = await _context.Transcriptions.FindAsync([transcriptionId], cancellationToken);
        if (transcription is not null)
        {
            _context.Transcriptions.Remove(transcription);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
