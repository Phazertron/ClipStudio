using ClipStudio.Core.Entities;

namespace ClipStudio.Core.Interfaces;

/// <summary>
/// Defines data access operations for <see cref="Transcription"/> and
/// <see cref="TranscriptionSegment"/> entities.
/// </summary>
public interface ITranscriptionRepository
{
    /// <summary>Returns all transcriptions for the given clip, ordered by creation date descending.</summary>
    Task<IReadOnlyList<Transcription>> GetByClipIdAsync(int clipId, CancellationToken cancellationToken = default);

    /// <summary>Returns the most recent transcription for the given clip, or null if none exist.</summary>
    Task<Transcription?> GetLatestByClipIdAsync(int clipId, CancellationToken cancellationToken = default);

    /// <summary>Returns all segments for the given transcription, ordered by <see cref="TranscriptionSegment.IndexNumber"/>.</summary>
    Task<IReadOnlyList<TranscriptionSegment>> GetSegmentsAsync(int transcriptionId, CancellationToken cancellationToken = default);

    /// <summary>Persists a new transcription and all of its segments to the repository.</summary>
    Task AddAsync(Transcription transcription, CancellationToken cancellationToken = default);

    /// <summary>Removes a transcription and its associated segments by identifier.</summary>
    Task DeleteAsync(int transcriptionId, CancellationToken cancellationToken = default);
}
