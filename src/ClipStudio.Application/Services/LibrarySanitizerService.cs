using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ClipStudio.Application.Services;

/// <summary>
/// Repairs the media-cache directory by regenerating any missing thumbnail or preview-strip
/// files for existing clips and highlights, and removing orphaned files that are no longer
/// referenced by any clip in the database.
/// </summary>
public sealed class LibrarySanitizerService : ILibrarySanitizerService
{
    private readonly IClipRepository _clips;
    private readonly IHighlightRepository _highlights;
    private readonly IMediaService _media;
    private readonly ISettingsService _settings;
    private readonly ITranscriptionRepository _transcriptions;
    private readonly AppDataPaths _paths;
    private readonly ILogger<LibrarySanitizerService> _logger;

    /// <summary>Initializes a new instance of <see cref="LibrarySanitizerService"/>.</summary>
    public LibrarySanitizerService(
        IClipRepository clips,
        IHighlightRepository highlights,
        IMediaService media,
        ISettingsService settings,
        ITranscriptionRepository transcriptions,
        AppDataPaths paths,
        ILogger<LibrarySanitizerService> logger)
    {
        _clips          = clips;
        _highlights     = highlights;
        _media          = media;
        _settings       = settings;
        _transcriptions = transcriptions;
        _paths          = paths;
        _logger         = logger;
    }

    /// <inheritdoc/>
    public async Task SanitizeAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Library sanitizer started.");

        var allClips    = await _clips.GetAllAsync(ct);
        var dataDir     = GetDataDirectory();
        var referencedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Protect thumbnails and strips of trashed (soft-deleted) clips from the orphan cleanup
        // below.  GetAllAsync only returns non-deleted clips, so without this step the media-cache
        // files of trashed clips would look like orphans and be deleted, making them unrecoverable.
        var trashedClips = await _clips.GetTrashedAsync(ct);
        foreach (var tc in trashedClips)
        {
            if (!string.IsNullOrEmpty(tc.ThumbnailPath))   referencedPaths.Add(tc.ThumbnailPath);
            if (!string.IsNullOrEmpty(tc.PreviewStripPath)) referencedPaths.Add(tc.PreviewStripPath);
        }
        var repaired    = 0;

        var thumbnailOffset = TimeSpan.FromSeconds(
            _settings.Current.ThumbnailOffsetSeconds);
        var stripFrameCount = _settings.Current.PreviewStripFrameCount;

        for (var i = 0; i < allClips.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var clip    = allClips[i];
            var changed = false;

            // ---- UTC timestamp repair (G5) ----
            // Clips imported before the Round-12A global UTC converter fix may have a local-time
            // value stored as if it were UTC.  Heuristic: if the file exists, interpret the stored
            // CreatedAt as local time, convert to UTC, and compare against the file's creation
            // time.  When the converted value is within 60 seconds of the file timestamp (i.e. it
            // matches), but the raw stored value differs by ≥30 minutes (indicating a UTC offset
            // was not applied), update CreatedAt to the correct UTC value.
            if (File.Exists(clip.FilePath))
            {
                try
                {
                    var fileCreatedUtc = File.GetCreationTimeUtc(clip.FilePath);
                    var assumedLocal   = DateTime.SpecifyKind(clip.CreatedAt, DateTimeKind.Local);
                    var assumedUtc     = assumedLocal.ToUniversalTime();

                    var offsetMatch = Math.Abs((assumedUtc - fileCreatedUtc).TotalSeconds) <= 60;
                    var rawMismatch = Math.Abs((clip.CreatedAt - fileCreatedUtc).TotalMinutes) >= 30;

                    if (offsetMatch && rawMismatch)
                    {
                        var oldValue   = clip.CreatedAt;
                        clip.CreatedAt = assumedUtc;
                        changed = true;
                        repaired++;
                        _logger.LogDebug("UTC timestamp repaired for clip {Id}: {OldUtc} → {NewUtc}",
                            clip.Id, oldValue, assumedUtc);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "UTC timestamp check failed for clip {Id}.", clip.Id);
                }
            }

            if (!File.Exists(clip.FilePath))
            {
                // Source file missing. If the clip's own trash path exists the file was moved to
                // the app trash but the DB record was not marked as deleted (e.g. due to a prior
                // crash or EF tracking bug). Repair by marking the record deleted so it appears
                // in the Trash page rather than as a ghost in the Library.
                if (!string.IsNullOrEmpty(clip.TrashPath) && File.Exists(clip.TrashPath))
                {
                    clip.IsDeleted = true;
                    clip.DeletedAt ??= DateTime.UtcNow;
                    await _clips.UpdateAsync(clip, ct);
                    repaired++;
                    _logger.LogWarning(
                        "Ghost clip {Id} repaired: file is in trash path but was not marked deleted.", clip.Id);
                    progress?.Report($"Ghost clip repaired: {clip.FileName}");
                    continue;
                }

                // TrashPath is null/empty but the file may still have been moved to the
                // .clipstudio_trash subfolder by an older broken version that did not persist
                // TrashPath. Try to locate the file by name (including timestamp-appended variants).
                var sourceDir = System.IO.Path.GetDirectoryName(clip.FilePath);
                if (!string.IsNullOrEmpty(sourceDir))
                {
                    var trashSubfolder = System.IO.Path.Combine(sourceDir, ".clipstudio_trash");
                    if (Directory.Exists(trashSubfolder))
                    {
                        var stem = System.IO.Path.GetFileNameWithoutExtension(clip.FilePath);
                        var foundPath = Directory.EnumerateFiles(trashSubfolder)
                            .FirstOrDefault(f =>
                            {
                                var fn = System.IO.Path.GetFileNameWithoutExtension(f);
                                return string.Equals(fn, stem, StringComparison.OrdinalIgnoreCase)
                                    || (fn.Length > stem.Length
                                        && fn.StartsWith(stem, StringComparison.OrdinalIgnoreCase)
                                        && fn[stem.Length] == '_');
                            });

                        if (foundPath is not null)
                        {
                            clip.IsDeleted = true;
                            clip.TrashPath = foundPath;
                            clip.DeletedAt ??= DateTime.UtcNow;
                            await _clips.UpdateAsync(clip, ct);
                            repaired++;
                            _logger.LogWarning(
                                "Ghost clip {Id} repaired: found file in trash subfolder at '{Path}'.",
                                clip.Id, foundPath);
                            progress?.Report($"Ghost clip repaired: {clip.FileName}");
                            continue;
                        }
                    }
                }

                // Source file truly gone — mark broken so the library shows a red overlay.
                if (!clip.IsBroken)
                {
                    clip.IsBroken = true;
                    await _clips.UpdateAsync(clip, ct);
                    repaired++;
                    _logger.LogWarning("Clip {Id} marked broken: source file not found at '{Path}'.", clip.Id, clip.FilePath);
                    progress?.Report($"Broken clip flagged: {clip.FileName}");
                }
                continue;
            }

            // File exists — clear any stale IsBroken flag (e.g. set by watcher during a temporary move).
            if (clip.IsBroken)
            {
                clip.IsBroken = false;
                changed = true;
                repaired++;
                _logger.LogInformation("Clip {Id} IsBroken cleared: source file found at '{Path}'.", clip.Id, clip.FilePath);
            }

            // Track existing paths for orphan detection even if regeneration is not needed.
            if (!string.IsNullOrEmpty(clip.ThumbnailPath))
                referencedPaths.Add(clip.ThumbnailPath);
            if (!string.IsNullOrEmpty(clip.PreviewStripPath))
                referencedPaths.Add(clip.PreviewStripPath);

            // ---- Thumbnail ----
            if (string.IsNullOrEmpty(clip.ThumbnailPath) || !File.Exists(clip.ThumbnailPath))
            {
                try
                {
                    var offset = TimeSpan.FromSeconds(
                        Math.Min(thumbnailOffset.TotalSeconds, clip.Duration.TotalSeconds * 0.1));

                    var path = await _media.GenerateThumbnailAsync(
                        clip.FilePath, dataDir, offset, cancellationToken: ct);

                    clip.ThumbnailPath = path;
                    referencedPaths.Add(path);
                    changed = true;
                    repaired++;
                    _logger.LogDebug("Regenerated thumbnail for clip {Id}.", clip.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to regenerate thumbnail for clip {Id}.", clip.Id);
                }
            }

            // ---- Preview strip ----
            if (string.IsNullOrEmpty(clip.PreviewStripPath) || !File.Exists(clip.PreviewStripPath))
            {
                try
                {
                    var path = await _media.GeneratePreviewStripAsync(
                        clip.FilePath, dataDir, stripFrameCount, ct);

                    clip.PreviewStripPath = path;
                    referencedPaths.Add(path);
                    changed = true;
                    repaired++;
                    _logger.LogDebug("Regenerated preview strip for clip {Id}.", clip.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to regenerate preview strip for clip {Id}.", clip.Id);
                }
            }

            if (changed)
                await _clips.UpdateAsync(clip, ct);

            // ---- Highlight thumbnails ----
            foreach (var highlight in clip.Highlights)
            {
                ct.ThrowIfCancellationRequested();

                if (!string.IsNullOrEmpty(highlight.ThumbnailPath))
                    referencedPaths.Add(highlight.ThumbnailPath);

                if (!string.IsNullOrEmpty(highlight.ThumbnailPath) && File.Exists(highlight.ThumbnailPath))
                    continue;

                try
                {
                    var midpoint = highlight.StartTime +
                        TimeSpan.FromSeconds((highlight.EndTime - highlight.StartTime).TotalSeconds / 2.0);

                    var path = await _media.GenerateThumbnailAsync(
                        clip.FilePath, dataDir, midpoint, $"hl{highlight.Id}", ct);

                    highlight.ThumbnailPath = path;
                    referencedPaths.Add(path);
                    await _highlights.UpdateAsync(highlight, ct);
                    repaired++;
                    _logger.LogDebug("Regenerated thumbnail for highlight {Id}.", highlight.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to regenerate thumbnail for highlight {Id}.", highlight.Id);
                }
            }

            if ((i + 1) % 10 == 0 || i == allClips.Count - 1)
                progress?.Report($"Checked {i + 1}/{allClips.Count} clips, {repaired} regenerated...");
        }

        // ---- Orphan cleanup ----
        var deleted = 0;
        if (Directory.Exists(dataDir))
        {
            foreach (var file in Directory.EnumerateFiles(dataDir))
            {
                ct.ThrowIfCancellationRequested();

                if (!referencedPaths.Contains(file))
                {
                    try
                    {
                        File.Delete(file);
                        deleted++;
                        _logger.LogDebug("Deleted orphan cache file: {File}", file);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete orphan cache file: {File}", file);
                    }
                }
            }
        }

        // ---- Audio-cache orphan cleanup ----
        // The audio cache directory is exclusively used for files named clip_{id}_audio_preview.mkv.
        // Delete every file that either does not match that pattern or whose clip ID is not in the
        // active clip set (covers renamed copies, stale files, and clips that have been deleted).
        var audioCacheDir  = GetAudioCacheDirectory();
        var validClipIds   = new HashSet<int>(allClips.Select(c => c.Id));
        var audioCleaned   = 0;
        if (Directory.Exists(audioCacheDir))
        {
            foreach (var file in Directory.EnumerateFiles(audioCacheDir))
            {
                ct.ThrowIfCancellationRequested();

                var name = System.IO.Path.GetFileNameWithoutExtension(file);
                // Expected pattern: clip_{id}_audio_preview — any other file is an orphan.
                var isValidPattern = false;
                if (name.StartsWith("clip_", StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith("_audio_preview", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(name["clip_".Length..^"_audio_preview".Length], out var clipId))
                {
                    isValidPattern = validClipIds.Contains(clipId);
                }

                if (!isValidPattern)
                {
                    try
                    {
                        File.Delete(file);
                        audioCleaned++;
                        _logger.LogDebug("Deleted orphan audio cache file: {File}", file);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete orphan audio cache file: {File}", file);
                    }
                }
            }
        }

        // ---- SRT orphan cleanup ----
        // Load all transcription DB records (no segments needed).
        // Delete the SRT file on disk and the DB record for any transcription whose clip no
        // longer exists in either the active or trashed set.  Trashed clips keep their
        // transcriptions so they survive a restore.
        var srtCleaned = 0;
        try
        {
            var trashedClipIds    = new HashSet<int>(trashedClips.Select(c => c.Id));
            var protectedClipIds  = new HashSet<int>(validClipIds.Concat(trashedClipIds));
            var allTranscriptions = await _transcriptions.GetAllAsync(ct);
            var knownSrtPaths     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var transcription in allTranscriptions)
            {
                ct.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(transcription.SrtFilePath))
                    knownSrtPaths.Add(transcription.SrtFilePath);

                if (!protectedClipIds.Contains(transcription.ClipId))
                {
                    // Clip permanently deleted — clean up SRT file and orphan DB record.
                    if (!string.IsNullOrWhiteSpace(transcription.SrtFilePath)
                        && File.Exists(transcription.SrtFilePath))
                    {
                        try
                        {
                            File.Delete(transcription.SrtFilePath);
                            srtCleaned++;
                            _logger.LogDebug("Deleted orphan SRT file: {Path}", transcription.SrtFilePath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete orphan SRT file: {Path}", transcription.SrtFilePath);
                        }
                    }

                    await _transcriptions.DeleteAsync(transcription.Id, ct);
                }
            }

            // Scan the configured SRT output folder for .srt files not in any DB record.
            var srtFolderCleaned = 0;
            var srtFolder = _settings.Current.TranscriptionSrtFolder;
            if (!string.IsNullOrWhiteSpace(srtFolder) && Directory.Exists(srtFolder))
            {
                foreach (var file in Directory.EnumerateFiles(srtFolder, "*.srt"))
                {
                    ct.ThrowIfCancellationRequested();

                    if (!knownSrtPaths.Contains(file))
                    {
                        try
                        {
                            File.Delete(file);
                            srtFolderCleaned++;
                            _logger.LogDebug("Deleted untracked SRT file: {File}", file);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete untracked SRT file: {File}", file);
                        }
                    }
                }
            }

            srtCleaned += srtFolderCleaned;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SRT orphan cleanup encountered an error.");
        }

        var summary = $"Sanitize complete: {repaired} file(s) regenerated, {deleted} orphan(s) removed, "
                    + $"{audioCleaned} orphan audio cache file(s) removed, "
                    + $"{srtCleaned} orphan SRT file(s) removed.";
        progress?.Report(summary);
        _logger.LogInformation("{Summary}", summary);
    }

    private string GetAudioCacheDirectory()
    {
        Directory.CreateDirectory(_paths.AudioCachePath);
        return _paths.AudioCachePath;
    }

    private string GetDataDirectory()
    {
        Directory.CreateDirectory(_paths.MediaCachePath);
        return _paths.MediaCachePath;
    }
}
