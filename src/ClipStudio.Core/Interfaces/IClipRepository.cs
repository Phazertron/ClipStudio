using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Models;

namespace ClipStudio.Core.Interfaces;

/// <summary>
/// Defines data access operations for <see cref="Clip"/> entities.
/// </summary>
public interface IClipRepository
{
    /// <summary>Returns the clip with the given identifier, including its tags and highlights, or null if not found.</summary>
    Task<Clip?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Returns all clips that match the given status filter.</summary>
    Task<IReadOnlyList<Clip>> GetByStatusAsync(ClipStatus status, CancellationToken cancellationToken = default);

    /// <summary>Returns all clips, ordered by creation date descending.</summary>
    Task<IReadOnlyList<Clip>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all clips that have at least one tag or highlight matching any of the given tag identifiers.</summary>
    Task<IReadOnlyList<Clip>> GetByTagsAsync(IEnumerable<int> tagIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the clips matching every criterion of <paramref name="query"/> that the data store
    /// can evaluate, ordered by creation date descending.
    /// </summary>
    /// <remarks>
    /// Free-text search (<see cref="ClipSearchQuery.SearchText"/> and
    /// <see cref="ClipSearchQuery.SearchCaptions"/>) is deliberately not applied here: it spans
    /// transcription segments and needs culture-aware comparison, so the caller applies it to the
    /// returned candidates. Every set-based criterion - tags, players, exclusions, status, rating,
    /// favourite, duration, creation date and highlight presence - is evaluated by the store.
    /// <see cref="ClipSearchQuery.TagIds"/> is used verbatim: the caller expands tag descendants
    /// beforehand when <see cref="ClipSearchQuery.IncludeTagDescendants"/> is set.
    /// </remarks>
    /// <param name="query">The filter to apply.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The matching clips, with tags, players and highlights loaded.</returns>
    Task<IReadOnlyList<Clip>> SearchAsync(
        ClipSearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a new clip to the repository.</summary>
    Task AddAsync(Clip clip, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing clip.</summary>
    Task UpdateAsync(Clip clip, CancellationToken cancellationToken = default);

    /// <summary>
    /// Directly inserts a <see cref="ClipTag"/> row for the given clip and tag.
    /// Idempotent — silently does nothing if the assignment already exists.
    /// </summary>
    Task AddClipTagAsync(int clipId, int tagId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Directly removes the <see cref="ClipTag"/> row for the given clip and tag.
    /// Idempotent — silently does nothing if the assignment does not exist.
    /// </summary>
    Task RemoveClipTagAsync(int clipId, int tagId, CancellationToken cancellationToken = default);

    /// <summary>Removes a clip from the repository by its identifier.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Returns true if a clip with the given file path already exists in the library.</summary>
    Task<bool> ExistsByFilePathAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>Returns all clips that have been moved to the trash (IsDeleted == true), ordered by deletion date descending.</summary>
    Task<IReadOnlyList<Clip>> GetTrashedAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all clips (including soft-deleted) associated with the given source folder.</summary>
    Task<IReadOnlyList<Clip>> GetBySourceFolderIdAsync(int folderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the clip whose <see cref="Clip.FilePath"/> matches the given path (case-insensitive),
    /// or <see langword="null"/> if no such clip exists.
    /// </summary>
    Task<Clip?> GetByFilePathAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every clip whose <see cref="Clip.FileHash"/> matches the given hash.
    /// </summary>
    /// <remarks>
    /// More than one clip can share a quick hash - it screens rather than proves - so the caller
    /// confirms a real match by comparing full hashes. Soft-deleted clips are excluded: a file in
    /// the trash should not block re-importing the same recording.
    /// </remarks>
    /// <param name="fileHash">The quick hash to look up.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The clips carrying that hash, empty when none do.</returns>
    Task<IReadOnlyList<Clip>> GetByFileHashAsync(string fileHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every clip whose file name matches the given one, ignoring case.
    /// </summary>
    /// <remarks>
    /// Two recordings from different source folders can share a name without sharing a path -
    /// "Replay.mp4" in two folders, say - which is worth telling the user about whether or not
    /// the contents match. Soft-deleted clips are excluded.
    /// </remarks>
    /// <param name="fileName">The file name to look up, without its directory.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The clips carrying that name, empty when none do.</returns>
    Task<IReadOnlyList<Clip>> GetByFileNameAsync(string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the clips that carry no content hash and so take no part in duplicate detection.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>How many live clips have no hash.</returns>
    Task<int> CountWithoutFileHashAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically increments <see cref="Clip.PlayCount"/> by one for the given clip identifier.
    /// No-ops silently if the clip does not exist.
    /// </summary>
    Task IncrementPlayCountAsync(int clipId, CancellationToken cancellationToken = default);
}
