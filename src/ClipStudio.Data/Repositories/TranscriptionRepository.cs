using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;
using ClipStudio.Core.Models;
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
    public async Task<IReadOnlyList<Transcription>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _context.Transcriptions
            .AsNoTracking()
            .ToListAsync(cancellationToken);

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

    /// <inheritdoc/>
    public async Task<IReadOnlyList<int>> SearchClipIdsBySegmentTextAsync(string searchText, CancellationToken cancellationToken = default)
        => await _context.TranscriptionSegments
            .Where(s => EF.Functions.Like(s.Text, $"%{searchText}%"))
            .Select(s => s.Transcription.ClipId)
            .Distinct()
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CaptionMatch>> SearchSegmentsAsync(
        string searchText, int limit, CancellationToken cancellationToken = default)
    {
        // Milliseconds are projected raw and converted after materialising: TimeSpan.FromMilliseconds
        // carries an optional parameter, which an expression tree cannot contain.
        var rows = await _context.TranscriptionSegments
            .AsNoTracking()
            .Where(s => EF.Functions.Like(s.Text, $"%{searchText}%"))
            .Where(s => !s.Transcription.Clip.IsDeleted)
            .OrderBy(s => s.StartMs)
            .Take(limit)
            .Select(s => new
            {
                s.Transcription.ClipId,
                s.Transcription.Clip.FileName,
                s.StartMs,
                s.Text,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new CaptionMatch(
                r.ClipId, r.FileName, TimeSpan.FromMilliseconds(r.StartMs), r.Text))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task UpdateSegmentAsync(int segmentId, string newText, CancellationToken cancellationToken = default)
    {
        var segment = await _context.TranscriptionSegments.FindAsync([segmentId], cancellationToken);
        if (segment is null) return;

        segment.Text = newText;
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> DeleteByClipIdAsync(int clipId, CancellationToken cancellationToken = default)
    {
        var transcriptions = await _context.Transcriptions
            .Where(t => t.ClipId == clipId)
            .ToListAsync(cancellationToken);

        var srtPaths = transcriptions
            .Select(t => t.SrtFilePath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        if (transcriptions.Count > 0)
        {
            _context.Transcriptions.RemoveRange(transcriptions);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return srtPaths;
    }
}
