using ClipStudio.Core.Entities;
using ClipStudio.Core.Models;

namespace ClipStudio.Core.Interfaces;

/// <summary>
/// Defines data access operations for <see cref="Transcription"/> and
/// <see cref="TranscriptionSegment"/> entities.
/// </summary>
public interface ITranscriptionRepository
{
    /// <summary>Returns every transcription record in the repository (without segments), for use by sanitizer passes.</summary>
    Task<IReadOnlyList<Transcription>> GetAllAsync(CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Removes all transcriptions (and their segments) for the given clip and returns
    /// the SRT file paths that were stored so the caller can delete the files on disk.
    /// </summary>
    Task<IReadOnlyList<string>> DeleteByClipIdAsync(int clipId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the distinct clip identifiers whose transcription segments contain the given text
    /// (case-insensitive substring match).  Used for caption-inclusive search.
    /// </summary>
    Task<IReadOnlyList<int>> SearchClipIdsBySegmentTextAsync(string searchText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds transcript lines containing the given text, with enough context to display and seek to.
    /// </summary>
    /// <param name="searchText">The text to look for.</param>
    /// <param name="limit">The most results to return.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The matching lines, earliest first.</returns>
    /// <remarks>
    /// <see cref="SearchClipIdsBySegmentTextAsync"/> answers "which clips mention this", which is
    /// enough to filter the library. This answers "where, and what was said", which is what a
    /// search result has to show.
    /// </remarks>
    Task<IReadOnlyList<CaptionMatch>> SearchSegmentsAsync(
        string searchText, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the text of a single <see cref="TranscriptionSegment"/> identified by its primary key.
    /// No-op when the segment is not found.
    /// </summary>
    /// <param name="segmentId">The primary key of the segment to update.</param>
    /// <param name="newText">The corrected text to store.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task UpdateSegmentAsync(int segmentId, string newText, CancellationToken cancellationToken = default);
}
