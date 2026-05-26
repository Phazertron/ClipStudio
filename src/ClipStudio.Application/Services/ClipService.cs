using System.IO;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ClipStudio.Application.Services;

/// <summary>
/// Application service for managing clips in the library.
/// </summary>
public sealed class ClipService : IClipService
{
    private readonly IClipRepository _clips;
    private readonly ITagRepository _tags;
    private readonly IRecycleBinService _recycleBin;
    private readonly ITranscriptionRepository _transcriptions;
    private readonly ILogger<ClipService> _logger;

    /// <summary>Initializes a new instance of <see cref="ClipService"/>.</summary>
    public ClipService(
        IClipRepository clips,
        ITagRepository tags,
        IRecycleBinService recycleBin,
        ITranscriptionRepository transcriptions,
        ILogger<ClipService> logger)
    {
        _clips          = clips;
        _tags           = tags;
        _recycleBin     = recycleBin;
        _transcriptions = transcriptions;
        _logger         = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Clip>> SearchAsync(
        ClipSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        // Resolve tag IDs including descendants when requested.
        var effectiveTagIds = new List<int>(query.TagIds);
        if (query.IncludeTagDescendants && query.TagIds.Count > 0)
        {
            foreach (var tagId in query.TagIds)
            {
                var descendants = await _tags.GetDescendantIdsAsync(tagId, cancellationToken);
                effectiveTagIds.AddRange(descendants);
            }
        }

        IReadOnlyList<Clip> candidates = effectiveTagIds.Count > 0
            ? await _clips.GetByTagsAsync(effectiveTagIds, cancellationToken)
            : await _clips.GetAllAsync(cancellationToken);

        // Fetch caption-matched clip IDs upfront so they can be used in the synchronous Where chain.
        // This is only executed when the user has explicitly opted in to caption search.
        HashSet<int>? captionIds = null;
        if (query.SearchCaptions && !string.IsNullOrEmpty(query.SearchText))
        {
            var ids = await _transcriptions.SearchClipIdsBySegmentTextAsync(query.SearchText, cancellationToken);
            captionIds = new HashSet<int>(ids);
        }

        // Apply remaining in-memory filters.
        return candidates
            .Where(c => query.Status == null || c.Status == query.Status)
            .Where(c => !query.ExcludeArchived || query.Status != null || c.Status != ClipStatus.Archived)
            .Where(c => query.CreatedFrom == null || c.CreatedAt >= query.CreatedFrom)
            .Where(c => query.CreatedTo == null || c.CreatedAt <= query.CreatedTo)
            .Where(c => c.Rating >= query.MinRating)
            .Where(c => query.IsFavourite == null || c.IsFavourite == query.IsFavourite)
            .Where(c => query.HasHighlights == null ||
                        (query.HasHighlights == true ? c.Highlights.Count > 0 : c.Highlights.Count == 0))
            .Where(c => query.MinDuration == null || c.Duration >= query.MinDuration)
            .Where(c => query.MaxDuration == null || c.Duration <= query.MaxDuration)
            .Where(c => string.IsNullOrEmpty(query.SearchText) ||
                        c.FileName.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase) ||
                        (c.Notes != null && c.Notes.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase)) ||
                        c.Highlights.Any(h =>
                            h.Label != null && h.Label.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase)) ||
                        (captionIds != null && captionIds.Contains(c.Id)))
            .Where(c => query.PlayerIds.Count == 0 ||
                        c.ClipPlayers.Any(cp => query.PlayerIds.Contains(cp.PlayerId)))
            .Where(c => query.ExcludedTagIds.Count == 0 ||
                        (!c.ClipTags.Any(ct => query.ExcludedTagIds.Contains(ct.TagId)) &&
                         !c.Highlights.Any(h => h.HighlightTags.Any(ht => query.ExcludedTagIds.Contains(ht.TagId)))))
            .Where(c => query.ExcludedPlayerIds.Count == 0 ||
                        !c.ClipPlayers.Any(cp => query.ExcludedPlayerIds.Contains(cp.PlayerId)))
            .ToList();
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Clip>> GetUnreviewedAsync(CancellationToken cancellationToken = default)
        => _clips.GetByStatusAsync(ClipStatus.Unreviewed, cancellationToken);

    /// <inheritdoc/>
    public Task<Clip?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => _clips.GetByIdAsync(id, cancellationToken);

    /// <inheritdoc/>
    public async Task SetStatusAsync(int clipId, ClipStatus status, CancellationToken cancellationToken = default)
    {
        var clip = await RequireClipAsync(clipId, cancellationToken);
        clip.Status = status;
        await _clips.UpdateAsync(clip, cancellationToken);
        _logger.LogInformation("Clip {ClipId} status set to {Status}.", clipId, status);
    }

    /// <inheritdoc/>
    public async Task SetRatingAsync(int clipId, int rating, CancellationToken cancellationToken = default)
    {
        if (rating < 0 || rating > 5)
            throw new ArgumentOutOfRangeException(nameof(rating), "Rating must be between 0 and 5.");

        var clip = await RequireClipAsync(clipId, cancellationToken);
        clip.Rating = rating;
        await _clips.UpdateAsync(clip, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task ToggleFavouriteAsync(int clipId, CancellationToken cancellationToken = default)
    {
        var clip = await RequireClipAsync(clipId, cancellationToken);
        clip.IsFavourite = !clip.IsFavourite;
        await _clips.UpdateAsync(clip, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task SetNotesAsync(int clipId, string notes, CancellationToken cancellationToken = default)
    {
        var clip = await RequireClipAsync(clipId, cancellationToken);
        clip.Notes = notes;
        await _clips.UpdateAsync(clip, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task AddTagAsync(int clipId, int tagId, CancellationToken cancellationToken = default)
    {
        // Use a direct INSERT rather than load-modify-UpdateAsync, because UpdateAsync uses
        // context.Update() on a disconnected graph which marks new ClipTag rows as Modified
        // (not Added) and the no-payload UPDATE is a silent no-op in SQLite.
        await _clips.AddClipTagAsync(clipId, tagId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task RemoveTagAsync(int clipId, int tagId, CancellationToken cancellationToken = default)
    {
        await _clips.RemoveClipTagAsync(clipId, tagId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task ConfirmGameTagAsync(int clipId, int gameTagId, CancellationToken cancellationToken = default)
    {
        var clip = await RequireClipAsync(clipId, cancellationToken);

        // Clear suggested game name and persist via UpdateAsync (Clip entity only).
        clip.SuggestedGameName = null;
        await _clips.UpdateAsync(clip, cancellationToken);

        // Insert the game tag directly to avoid the disconnected-graph Modified-state bug.
        await _clips.AddClipTagAsync(clipId, gameTagId, cancellationToken);

        _logger.LogInformation("Game tag {TagId} confirmed for clip {ClipId}.", gameTagId, clipId);
    }

    /// <inheritdoc/>
    public async Task RenameAsync(int clipId, string newFileName, bool destructive, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newFileName))
            throw new ArgumentException("File name must not be empty.", nameof(newFileName));

        var clip = await RequireClipAsync(clipId, cancellationToken);

        if (destructive)
        {
            var directory = Path.GetDirectoryName(clip.FilePath) ?? string.Empty;
            var newPath   = Path.Combine(directory, newFileName);
            File.Move(clip.FilePath, newPath);
            clip.FilePath = newPath;
            _logger.LogInformation("Clip {ClipId} file moved on disk to: {Path}", clipId, newPath);
        }

        clip.FileName = newFileName;
        await _clips.UpdateAsync(clip, cancellationToken);
        _logger.LogInformation("Clip {ClipId} renamed to {FileName}.", clipId, newFileName);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(int clipId, CancellationToken cancellationToken = default)
    {
        await _clips.DeleteAsync(clipId, cancellationToken);
        _logger.LogInformation("Clip {ClipId} removed from library.", clipId);
    }

    /// <inheritdoc/>
    public async Task TrashAsync(int clipId, CancellationToken cancellationToken = default)
    {
        var clip = await RequireClipAsync(clipId, cancellationToken);

        if (clip.IsDeleted)
            return;

        // If the source file is already missing (broken clip), skip the file move and
        // just mark the DB record as deleted so the user can still "trash" it from the UI.
        if (!File.Exists(clip.FilePath))
        {
            clip.IsDeleted = true;
            clip.DeletedAt = DateTime.UtcNow;
            clip.TrashPath = null;
            await _clips.UpdateAsync(clip, cancellationToken);
            _logger.LogWarning("Clip {ClipId} trashed (source file already missing, DB-only).", clipId);
            return;
        }

        var originalDir = Path.GetDirectoryName(clip.FilePath) ?? string.Empty;
        var trashDir    = Path.Combine(originalDir, ".clipstudio_trash");
        Directory.CreateDirectory(trashDir);

        var trashPath = Path.Combine(trashDir, Path.GetFileName(clip.FilePath));

        // Avoid name collision inside the trash folder.
        if (File.Exists(trashPath))
        {
            var stem  = Path.GetFileNameWithoutExtension(clip.FilePath);
            var ext   = Path.GetExtension(clip.FilePath);
            trashPath = Path.Combine(trashDir, $"{stem}_{DateTime.UtcNow:yyyyMMddHHmmss}{ext}");
        }

        // Update the DB record BEFORE moving the file so that the file-watcher's Deleted event
        // (which fires the moment File.Move removes the file from the source folder) sees
        // IsDeleted=true and skips marking the clip as broken — preventing a lost-update race
        // where SetBrokenByFilePathAsync loads the old state and overwrites the TrashPath.
        clip.IsDeleted = true;
        clip.DeletedAt = DateTime.UtcNow;
        clip.TrashPath = trashPath;
        clip.IsBroken  = false;
        await _clips.UpdateAsync(clip, cancellationToken);

        try
        {
            File.Move(clip.FilePath, trashPath);
        }
        catch
        {
            // Roll back the DB change so the clip stays visible in the library.
            clip.IsDeleted = false;
            clip.DeletedAt = null;
            clip.TrashPath = null;
            await _clips.UpdateAsync(clip, cancellationToken);
            throw;
        }

        _logger.LogInformation("Clip {ClipId} moved to trash: {TrashPath}", clipId, trashPath);
    }

    /// <inheritdoc/>
    public async Task RestoreFromTrashAsync(int clipId, CancellationToken cancellationToken = default)
    {
        var clip = await _clips.GetByIdAsync(clipId, cancellationToken)
            ?? throw new InvalidOperationException($"Clip with id {clipId} was not found.");

        if (!clip.IsDeleted)
            return;

        // TrashPath can be null when the clip was trashed while its source file was already
        // missing, or when a prior race condition left the DB record without a TrashPath.
        // Try to locate the file in the expected .clipstudio_trash subfolder as a fallback.
        if (string.IsNullOrEmpty(clip.TrashPath))
        {
            var expectedTrashDir = Path.Combine(
                Path.GetDirectoryName(clip.FilePath) ?? string.Empty,
                ".clipstudio_trash");
            var stem = Path.GetFileNameWithoutExtension(clip.FilePath);
            var foundPath = Directory.Exists(expectedTrashDir)
                ? Directory.EnumerateFiles(expectedTrashDir).FirstOrDefault(f =>
                {
                    var fn = Path.GetFileNameWithoutExtension(f);
                    return string.Equals(fn, stem, StringComparison.OrdinalIgnoreCase)
                        || (fn.Length > stem.Length
                            && fn.StartsWith(stem, StringComparison.OrdinalIgnoreCase)
                            && fn[stem.Length] == '_');
                })
                : null;

            if (foundPath is null)
            {
                // File not found anywhere — mark as untrashed with no file so it appears as broken.
                clip.IsDeleted = false;
                clip.DeletedAt = null;
                clip.IsBroken  = true;
                await _clips.UpdateAsync(clip, cancellationToken);
                _logger.LogWarning(
                    "Clip {ClipId} restored (DB-only): TrashPath was null and file not found in trash folder.",
                    clipId);
                return;
            }

            clip.TrashPath = foundPath;
            _logger.LogWarning(
                "Clip {ClipId} TrashPath was null; located file at '{Path}' for restore.",
                clipId, foundPath);
        }

        if (!File.Exists(clip.TrashPath))
            throw new FileNotFoundException("Trash file not found.", clip.TrashPath);

        File.Move(clip.TrashPath, clip.FilePath);

        clip.IsDeleted = false;
        clip.DeletedAt = null;
        clip.TrashPath = null;
        clip.IsBroken  = false;
        await _clips.UpdateAsync(clip, cancellationToken);
        _logger.LogInformation("Clip {ClipId} restored from trash.", clipId);
    }

    /// <inheritdoc/>
    public async Task PermanentlyDeleteAsync(int clipId, CancellationToken cancellationToken = default)
    {
        var clip = await _clips.GetByIdAsync(clipId, cancellationToken)
            ?? throw new InvalidOperationException($"Clip with id {clipId} was not found.");

        if (!string.IsNullOrEmpty(clip.TrashPath) && File.Exists(clip.TrashPath))
            _recycleBin.TryMoveToRecycleBin(clip.TrashPath);

        await DeleteTranscriptionsForClipAsync(clipId, cancellationToken);
        await _clips.DeleteAsync(clipId, cancellationToken);
        _logger.LogInformation("Clip {ClipId} permanently deleted.", clipId);
    }

    /// <inheritdoc/>
    public async Task TrueDeleteAsync(int clipId, CancellationToken cancellationToken = default)
    {
        var clip = await _clips.GetByIdAsync(clipId, cancellationToken)
            ?? throw new InvalidOperationException($"Clip with id {clipId} was not found.");

        if (!string.IsNullOrEmpty(clip.TrashPath) && File.Exists(clip.TrashPath))
        {
            try
            {
                File.Delete(clip.TrashPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete trash file for clip {ClipId}.", clipId);
            }
        }

        await DeleteTranscriptionsForClipAsync(clipId, cancellationToken);
        await _clips.DeleteAsync(clipId, cancellationToken);
        _logger.LogInformation("Clip {ClipId} irrecoverably deleted (no recycle bin).", clipId);
    }

    /// <summary>
    /// Deletes all transcription DB records for the given clip and removes the associated
    /// SRT files from disk.  Called before the clip record itself is deleted.
    /// </summary>
    private async Task DeleteTranscriptionsForClipAsync(int clipId, CancellationToken cancellationToken)
    {
        try
        {
            var srtPaths = await _transcriptions.DeleteByClipIdAsync(clipId, cancellationToken);
            foreach (var path in srtPaths)
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    try { File.Delete(path); }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete SRT file {Path} for clip {ClipId}.", path, clipId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete transcriptions for clip {ClipId}.", clipId);
        }
    }

    /// <inheritdoc/>
    public async Task PurgeExpiredTrashAsync(int retentionDays = 30, bool sendToRecycleBin = true, CancellationToken cancellationToken = default)
    {
        var trashed = await _clips.GetTrashedAsync(cancellationToken);
        var cutoff  = DateTime.UtcNow.AddDays(-retentionDays);

        foreach (var clip in trashed)
        {
            if (clip.DeletedAt.HasValue && clip.DeletedAt.Value < cutoff)
            {
                if (sendToRecycleBin)
                    await PermanentlyDeleteAsync(clip.Id, cancellationToken);
                else
                    await TrueDeleteAsync(clip.Id, cancellationToken);

                _logger.LogInformation("Clip {ClipId} purged from trash (expired).", clip.Id);
            }
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Clip>> GetTrashedAsync(CancellationToken cancellationToken = default)
        => _clips.GetTrashedAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task BulkAddTagAsync(IEnumerable<int> clipIds, int tagId, CancellationToken cancellationToken = default)
    {
        foreach (var clipId in clipIds)
            await AddTagAsync(clipId, tagId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task BulkSetGameAsync(IEnumerable<int> clipIds, int gameTagId, CancellationToken cancellationToken = default)
    {
        foreach (var clipId in clipIds)
            await ConfirmGameTagAsync(clipId, gameTagId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task BulkTrashAsync(IEnumerable<int> clipIds, CancellationToken cancellationToken = default)
    {
        foreach (var clipId in clipIds)
            await TrashAsync(clipId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task ClearTagsAsync(int clipId, CancellationToken cancellationToken = default)
    {
        var clip = await _clips.GetByIdAsync(clipId, cancellationToken);
        if (clip is null) return;

        var tagIds = clip.ClipTags.Select(ct => ct.TagId).ToList();
        foreach (var tagId in tagIds)
            await _clips.RemoveClipTagAsync(clipId, tagId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task BulkClearGameAsync(IEnumerable<int> clipIds, CancellationToken cancellationToken = default)
    {
        foreach (var clipId in clipIds)
        {
            var clip = await _clips.GetByIdAsync(clipId, cancellationToken);
            if (clip is null) continue;

            var gameTagId = clip.ClipTags
                .Select(ct => ct.Tag)
                .Where(t => t?.Type == ClipStudio.Core.Enums.TagType.Game)
                .Select(t => t!.Id)
                .FirstOrDefault();

            if (gameTagId != 0)
                await _clips.RemoveClipTagAsync(clipId, gameTagId, cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task ArchiveBySourceFolderAsync(int folderId, CancellationToken cancellationToken = default)
    {
        // Collect only clip IDs — loading partial entities without navigation properties and
        // then calling UpdateAsync causes EF Core to cascade-delete related rows (ClipTags,
        // Highlights, etc.).  SetStatusAsync loads each clip fully before saving, so it is safe.
        var clips     = await _clips.GetBySourceFolderIdAsync(folderId, cancellationToken);
        var toArchive = clips
            .Where(c => !c.IsDeleted && c.Status != ClipStatus.Archived)
            .Select(c => c.Id)
            .ToList();

        foreach (var clipId in toArchive)
            await SetStatusAsync(clipId, ClipStatus.Archived, cancellationToken);

        _logger.LogInformation("Archived {Count} clip(s) for source folder {FolderId}.", toArchive.Count, folderId);
    }

    /// <inheritdoc/>
    public async Task UnarchiveBySourceFolderAsync(int folderId, CancellationToken cancellationToken = default)
    {
        var clips      = await _clips.GetBySourceFolderIdAsync(folderId, cancellationToken);
        var toRestore  = clips
            .Where(c => !c.IsDeleted && c.Status == ClipStatus.Archived)
            .Select(c => c.Id)
            .ToList();

        foreach (var clipId in toRestore)
            await SetStatusAsync(clipId, ClipStatus.Unreviewed, cancellationToken);

        _logger.LogInformation("Unarchived {Count} clip(s) for source folder {FolderId}.", toRestore.Count, folderId);
    }

    /// <inheritdoc/>
    public async Task WipeBySourceFolderAsync(int folderId, CancellationToken cancellationToken = default)
    {
        var clips = await _clips.GetBySourceFolderIdAsync(folderId, cancellationToken);

        foreach (var clip in clips)
        {
            TryDeleteFile(clip.ThumbnailPath);
            TryDeleteFile(clip.PreviewStripPath);
            await _clips.DeleteAsync(clip.Id, cancellationToken);
        }

        _logger.LogInformation("Wiped {Count} clip(s) for source folder {FolderId}.", clips.Count, folderId);
    }

    /// <inheritdoc/>
    public async Task SetBrokenByFilePathAsync(string filePath, bool isBroken, CancellationToken cancellationToken = default)
    {
        var clip = await _clips.GetByFilePathAsync(filePath, cancellationToken);
        if (clip is null)
            return;

        // Never mark a trashed clip as broken — it is intentionally absent from the source folder.
        if (clip.IsDeleted)
            return;

        if (clip.IsBroken == isBroken)
            return;

        clip.IsBroken = isBroken;
        await _clips.UpdateAsync(clip, cancellationToken);
        _logger.LogInformation("Clip {ClipId} ({FileName}) marked as {State}.",
            clip.Id, clip.FileName, isBroken ? "broken" : "repaired");
    }

    /// <inheritdoc/>
    public Task IncrementPlayCountAsync(int clipId, CancellationToken cancellationToken = default)
        => _clips.IncrementPlayCountAsync(clipId, cancellationToken);

    /// <inheritdoc/>
    public async Task RelocateAsync(int clipId, string newFilePath, int? newSourceFolderId = null, CancellationToken cancellationToken = default)
    {
        var clip = await RequireClipAsync(clipId, cancellationToken);

        clip.FilePath  = newFilePath;
        clip.FileName  = Path.GetFileName(newFilePath);
        clip.IsBroken  = false;

        if (newSourceFolderId.HasValue)
            clip.SourceFolderId = newSourceFolderId.Value;

        // If the clip was trashed as a missing-file entry (TrashPath null), restore it.
        if (clip.IsDeleted && string.IsNullOrEmpty(clip.TrashPath))
        {
            clip.IsDeleted = false;
            clip.DeletedAt = null;
        }

        await _clips.UpdateAsync(clip, cancellationToken);
        _logger.LogInformation("Clip {ClipId} relocated to '{NewPath}'.", clipId, newFilePath);
    }

    private void TryDeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try { File.Delete(path); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete cache file: {Path}", path); }
    }

    private async Task<Clip> RequireClipAsync(int clipId, CancellationToken cancellationToken)
    {
        var clip = await _clips.GetByIdAsync(clipId, cancellationToken);
        if (clip is null)
            throw new InvalidOperationException($"Clip with id {clipId} was not found.");
        return clip;
    }
}
