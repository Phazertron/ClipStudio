using ClipStudio.Application.Models;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;

namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Provides operations for managing clips in the library, including metadata updates,
/// tag assignments, and filtered searching.
/// </summary>
public interface IClipService
{
    /// <summary>Returns a filtered and sorted list of clips matching the given query.</summary>
    Task<IReadOnlyList<Clip>> SearchAsync(ClipSearchQuery query, CancellationToken cancellationToken = default);

    /// <summary>Returns all clips in Unreviewed status, ordered by import date descending.</summary>
    Task<IReadOnlyList<Clip>> GetUnreviewedAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the clip with the given identifier, including tags, highlights, and screenshots.</summary>
    Task<Clip?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Updates the review status of a clip.</summary>
    Task SetStatusAsync(int clipId, ClipStatus status, CancellationToken cancellationToken = default);

    /// <summary>Sets the star rating for a clip. Valid values are 0 (unrated) through 5.</summary>
    Task SetRatingAsync(int clipId, int rating, CancellationToken cancellationToken = default);

    /// <summary>Toggles the favourite flag on a clip.</summary>
    Task ToggleFavouriteAsync(int clipId, CancellationToken cancellationToken = default);

    /// <summary>Replaces the free-text notes on a clip.</summary>
    Task SetNotesAsync(int clipId, string notes, CancellationToken cancellationToken = default);

    /// <summary>Applies a tag directly to a clip at the clip level.</summary>
    Task AddTagAsync(int clipId, int tagId, CancellationToken cancellationToken = default);

    /// <summary>Removes a clip-level tag assignment.</summary>
    Task RemoveTagAsync(int clipId, int tagId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms the suggested game name for a clip by assigning the given Game tag,
    /// clearing <see cref="Clip.SuggestedGameName"/>, and marking the clip as Reviewed
    /// if it has no other pending actions.
    /// </summary>
    Task ConfirmGameTagAsync(int clipId, int gameTagId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a clip. In non-destructive mode only the database record is updated.
    /// In destructive mode the physical file is also renamed on disk and the stored path is updated.
    /// </summary>
    /// <param name="clipId">The identifier of the clip to rename.</param>
    /// <param name="newFileName">The new file name (including extension).</param>
    /// <param name="destructive">
    /// When <see langword="true"/>, the physical file is moved on disk in addition to updating the database.
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task RenameAsync(int clipId, string newFileName, bool destructive, CancellationToken cancellationToken = default);

    /// <summary>Removes a clip record from the library database. The source file on disk is not touched.</summary>
    Task DeleteAsync(int clipId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a clip's source file to the application trash folder and marks the clip as deleted.
    /// The clip record is retained in the database with IsDeleted set to true.
    /// </summary>
    Task TrashAsync(int clipId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a trashed clip by moving its file back to the original location and clearing the deleted flags.
    /// </summary>
    Task RestoreFromTrashAsync(int clipId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes a trashed clip by moving its file to the operating-system recycle bin
    /// (or deleting it directly if the recycle bin is unavailable), then removes the database record.
    /// </summary>
    Task PermanentlyDeleteAsync(int clipId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Irrecoverably deletes a trashed clip: removes the file directly from the trash folder
    /// without using the system recycle bin, then deletes the database record.
    /// This action cannot be undone.
    /// </summary>
    Task TrueDeleteAsync(int clipId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes all clips that have been in the trash for longer than <paramref name="retentionDays"/> days.
    /// </summary>
    /// <param name="retentionDays">Number of days before an item is eligible for purging.</param>
    /// <param name="sendToRecycleBin">
    /// When <c>true</c>, the file is sent to the OS Recycle Bin via <see cref="PermanentlyDeleteAsync"/>;
    /// when <c>false</c>, the file is irrecoverably deleted via <see cref="TrueDeleteAsync"/>.
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task PurgeExpiredTrashAsync(int retentionDays = 30, bool sendToRecycleBin = true, CancellationToken cancellationToken = default);

    /// <summary>Returns all clips currently in the trash, ordered by deletion date descending.</summary>
    Task<IReadOnlyList<Clip>> GetTrashedAsync(CancellationToken cancellationToken = default);

    /// <summary>Applies a tag to each of the given clips.</summary>
    Task BulkAddTagAsync(IEnumerable<int> clipIds, int tagId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a game tag on each of the given clips, replacing any existing game-type tag assignment.
    /// </summary>
    Task BulkSetGameAsync(IEnumerable<int> clipIds, int gameTagId, CancellationToken cancellationToken = default);

    /// <summary>Moves each of the given clips to the trash.</summary>
    Task BulkTrashAsync(IEnumerable<int> clipIds, CancellationToken cancellationToken = default);

    /// <summary>Removes ALL tag assignments (both general and game) from the specified clip.</summary>
    Task ClearTagsAsync(int clipId, CancellationToken cancellationToken = default);

    /// <summary>Removes the game-type tag assignment from each of the given clips, if present.</summary>
    Task BulkClearGameAsync(IEnumerable<int> clipIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes all non-deleted clips belonging to the given source folder,
    /// hiding them from the library while preserving all metadata, tags, highlights, and source files on disk.
    /// </summary>
    Task ArchiveBySourceFolderAsync(int folderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores all archived clips belonging to the given source folder to <see cref="ClipStatus.Unreviewed"/>.
    /// Called when the user re-enables an archived source folder.
    /// </summary>
    Task UnarchiveBySourceFolderAsync(int folderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently removes all clip database records belonging to the given source folder
    /// and deletes their media-cache files (thumbnails, preview strips).
    /// Source video files on disk are not touched.
    /// </summary>
    Task WipeBySourceFolderAsync(int folderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks or clears the broken state of a clip identified by its file path.
    /// Has no effect if no clip with that path exists in the library.
    /// </summary>
    /// <param name="filePath">The absolute path of the video file.</param>
    /// <param name="isBroken">
    /// <see langword="true"/> to mark the clip as broken; <see langword="false"/> to clear the flag.
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task SetBrokenByFilePathAsync(string filePath, bool isBroken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Increments the <see cref="ClipStudio.Core.Entities.Clip.PlayCount"/> of the specified clip by one.
    /// </summary>
    /// <param name="clipId">The identifier of the clip to update.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task IncrementPlayCountAsync(int clipId, CancellationToken cancellationToken = default);
}
