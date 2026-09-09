using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Application.Parsing;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ClipStudio.Application.Services;

/// <summary>
/// Orchestrates the clip import pipeline: validates the file, extracts metadata, generates
/// thumbnails and preview strips, parses the filename for a suggested game name, and persists
/// the clip to the library database.
/// After a clip is saved, any player marked <c>IsMe = true</c> is automatically tagged on the clip.
/// </summary>
public sealed class ImportService : IImportService
{
    private static readonly string[] SupportedExtensions =
        [".mp4", ".mkv", ".mov", ".webm", ".avi", ".flv", ".ts"];

    private readonly IClipRepository _clips;
    private readonly ISourceFolderRepository _folders;
    private readonly IMediaService _media;
    private readonly ISettingsService _settings;
    private readonly IPlayerRepository _players;
    private readonly IGameTagAliasService _gameAliases;
    private readonly ITranscriptionService _transcription;
    private readonly IFileSystem _fileSystem;
    private readonly AppDataPaths _paths;
    private readonly IFileHashService _fileHashes;
    private readonly ILogger<ImportService> _logger;

    /// <summary>Initializes a new instance of <see cref="ImportService"/>.</summary>
    public ImportService(
        IClipRepository clips,
        ISourceFolderRepository folders,
        IMediaService media,
        ISettingsService settings,
        IPlayerRepository players,
        IGameTagAliasService gameAliases,
        ITranscriptionService transcription,
        IFileSystem fileSystem,
        AppDataPaths paths,
        IFileHashService fileHashes,
        ILogger<ImportService> logger)
    {
        _clips = clips;
        _folders = folders;
        _media = media;
        _settings = settings;
        _players = players;
        _gameAliases = gameAliases;
        _transcription = transcription;
        _fileSystem = fileSystem;
        _paths = paths;
        _fileHashes = fileHashes;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ImportResult> ImportFileAsync(
        string filePath,
        int sourceFolderId,
        bool allowDuplicate = false,
        CancellationToken cancellationToken = default)
    {
        if (!ClipFileNameParser.IsSupportedVideoFile(filePath))
            return ImportResult.Failed($"Unsupported file type: {Path.GetExtension(filePath)}");

        if (!_fileSystem.FileExists(filePath))
            return ImportResult.Failed($"File not found: {filePath}");

        if (await _clips.ExistsByFilePathAsync(filePath, cancellationToken))
            return ImportResult.Skipped($"Already in library: {Path.GetFileName(filePath)}");

        // A repeated file name is checked first: it costs one indexed query, needs no hashing, and
        // is worth surfacing whether or not the contents turn out to match. Always on - the hashing
        // setting does not govern it.
        if (!allowDuplicate)
        {
            var sameName = await FindSameNameAsync(filePath, cancellationToken);
            if (sameName is not null)
            {
                _logger.LogInformation(
                    "Import stopped: {FilePath} shares a name with clip {ClipId} at '{Existing}'.",
                    filePath, sameName.Id, sameName.FilePath);
                return ImportResult.Duplicate(filePath, sameName, DuplicateMatchKind.FileName);
            }
        }

        // Hashing is the expensive part of an import - megabytes read from every file - so the
        // setting skips it outright rather than merely skipping the comparison. Done before the
        // thumbnail and preview strip so a duplicate costs a hash rather than the whole pipeline.
        string? fileHash = null;

        if (_settings.Current.ContentHashingEnabled)
        {
            fileHash = await TryComputeQuickHashAsync(filePath, cancellationToken);

            if (!allowDuplicate && fileHash is not null)
            {
                var existing = await FindDuplicateAsync(filePath, fileHash, cancellationToken);
                if (existing is not null)
                {
                    _logger.LogInformation(
                        "Import stopped: {FilePath} has the same contents as clip {ClipId} ({FileName}).",
                        filePath, existing.Id, existing.FileName);
                    return ImportResult.Duplicate(filePath, existing, DuplicateMatchKind.Content);
                }
            }
        }

        _logger.LogInformation("Importing clip: {FilePath}", filePath);

        try
        {
            var metadata = await _media.GetMetadataAsync(filePath, cancellationToken);
            var dataDirectory = GetDataDirectory();
            var clipStorageId = Guid.NewGuid().ToString("N");

            var thumbnailOffset = TimeSpan.FromSeconds(
                Math.Min(_settings.Current.ThumbnailOffsetSeconds, metadata.Duration.TotalSeconds * 0.1));

            var thumbnailPath = await _media.GenerateThumbnailAsync(
                filePath, dataDirectory, thumbnailOffset, cancellationToken: cancellationToken);

            var previewStripPath = await _media.GeneratePreviewStripAsync(
                filePath, dataDirectory, _settings.Current.PreviewStripFrameCount, cancellationToken);

            var suggestedGameName = ClipFileNameParser.ExtractGameName(filePath);

            // Timestamp priority: (1) OBS filename pattern, (2) embedded FFProbe creation_time,
            // (3) OS file creation time (least reliable — may be "now" after a file copy).
            var recordedAt = ClipFileNameParser.ExtractTimestamp(filePath)
                             ?? metadata.EmbeddedCreationTime
                             ?? _fileSystem.GetCreationTimeUtc(filePath);

            var clip = new Clip
            {
                SourceFolderId = sourceFolderId,
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                Duration = metadata.Duration,
                Resolution = metadata.Resolution,
                FileSizeBytes = metadata.FileSizeBytes,
                CreatedAt = recordedAt,
                ImportedAt = DateTime.UtcNow,
                ThumbnailPath = thumbnailPath,
                PreviewStripPath = previewStripPath,
                Status = ClipStatus.Unreviewed,
                SuggestedGameName = suggestedGameName,
                FileHash = fileHash
            };

            await _clips.AddAsync(clip, cancellationToken);
            _logger.LogInformation("Clip imported with Id={ClipId}.", clip.Id);

            // Auto-apply a game tag if a remembered alias exists for the suggested game name.
            if (!string.IsNullOrEmpty(suggestedGameName))
            {
                var alias = await _gameAliases.FindByAliasAsync(suggestedGameName, cancellationToken);
                if (alias is not null)
                {
                    await _clips.AddClipTagAsync(clip.Id, alias.TagId, cancellationToken);
                    clip.SuggestedGameName = null;
                    await _clips.UpdateAsync(clip, cancellationToken);
                    _logger.LogInformation(
                        "Game alias '{Alias}' auto-applied tag {TagId} to clip {ClipId}.",
                        suggestedGameName, alias.TagId, clip.Id);
                }
            }

            // Auto-tag "me" players on the newly imported clip (only if enabled in settings).
            if (_settings.Current.AutoApplyMePlayerOnImport)
            {
                var mePlayers = await _players.GetMePlayersAsync(cancellationToken);
                foreach (var player in mePlayers)
                    await _players.TagClipAsync(clip.Id, player.Id, cancellationToken);
            }

            // Auto-transcription: fire-and-forget based on the configured auto-mode.
            MaybeAutoTranscribe(clip.Id, metadata.AudioStreamCount, filePath);

            return ImportResult.Succeeded(clip);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import file: {FilePath}", filePath);
            return ImportResult.Failed($"Import error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ImportResult>> ScanFolderAsync(
        int sourceFolderId,
        IProgress<ImportProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var folder = await _folders.GetByIdAsync(sourceFolderId, cancellationToken);
        if (folder is null)
            throw new InvalidOperationException($"Source folder with id {sourceFolderId} was not found.");

        if (!_fileSystem.DirectoryExists(folder.Path))
        {
            _logger.LogWarning("Source folder path does not exist: {Path}", folder.Path);
            return [];
        }

        var files = _fileSystem.EnumerateFiles(folder.Path)
            .Where(ClipFileNameParser.IsSupportedVideoFile)
            .ToList();

        _logger.LogInformation("Scanning folder {Path}: {Count} video file(s) found.", folder.Path, files.Count);

        var results  = new List<ImportResult>(files.Count);
        var imported = 0;
        var skipped  = 0;
        var failed   = 0;

        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file   = files[i];
            // A folder scan cannot ask, so duplicates are reported in the results and left for the
            // caller to resolve rather than imported silently.
            var result = await ImportFileAsync(
                file, sourceFolderId, allowDuplicate: false, cancellationToken);
            results.Add(result);

            if (result.Success && result.Clip != null) imported++;
            else if (result.Success)                   skipped++;
            else                                       failed++;

            progress?.Report(new ImportProgressReport(
                TotalFiles:       files.Count,
                CurrentFileIndex: i + 1,
                CurrentFileName:  Path.GetFileName(file),
                Imported:         imported,
                Skipped:          skipped,
                Failed:           failed));
        }

        folder.LastScannedAt = DateTime.UtcNow;
        await _folders.UpdateAsync(folder, cancellationToken);

        return results;
    }

    /// <summary>
    /// Starts a background transcription task for the newly imported clip when auto-transcription
    /// on import is enabled.  The task is fire-and-forget; failures are only logged.
    /// </summary>
    private void MaybeAutoTranscribe(int clipId, int audioStreamCount, string filePath)
    {
        var s = _settings.Current;

        if (!s.TranscriptionEnabled
            || !s.TranscriptionAutoOnImport
            || string.IsNullOrWhiteSpace(s.TranscriptionModelPath))
            return;

        // Parse the configured index string (e.g. "0" or "0,2"), clamp to available stream count.
        var raw = string.IsNullOrWhiteSpace(s.TranscriptionAutoOnImportTrackIndices)
            ? "0"
            : s.TranscriptionAutoOnImportTrackIndices;

        var trackIndices = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => int.TryParse(token, out var idx) ? idx : -1)
            .Where(idx => idx >= 0 && idx < audioStreamCount)
            .Distinct()
            .OrderBy(idx => idx)
            .ToList();

        if (trackIndices.Count == 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await _transcription.TranscribeAsync(
                    clipId,
                    trackIndices,
                    s.TranscriptionModelPath,
                    s.TranscriptionBackend,
                    s.TranscriptionLanguage,
                    progress: null,
                    cancellationToken: default);

                _logger.LogInformation(
                    "Auto-transcription complete for clip {ClipId} ({FilePath}).", clipId, filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto-transcription failed for clip {ClipId}.", clipId);
            }
        });
    }

    /// <summary>
    /// Returns the media cache directory used for generated thumbnails and preview strips,
    /// creating it when it does not yet exist.
    /// </summary>
    /// <returns>The absolute path to the media cache directory.</returns>
    private string GetDataDirectory()
    {
        _fileSystem.CreateDirectory(_paths.MediaCachePath);
        return _paths.MediaCachePath;
    }

    /// <summary>
    /// Finds a live clip already in the library carrying the same file name.
    /// </summary>
    /// <remarks>
    /// The exact-path check above has already run, so anything found here is the same name in a
    /// different folder - which is what makes it worth reporting rather than skipping silently.
    /// </remarks>
    /// <param name="filePath">The file being imported.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The clip sharing the name, or null when none does.</returns>
    private async Task<Clip?> FindSameNameAsync(string filePath, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(filePath);
        var matches  = await _clips.GetByFileNameAsync(fileName, cancellationToken);

        return matches.FirstOrDefault(c =>
            !string.Equals(c.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Hashes a file, returning null rather than failing the import when it cannot be read.
    /// </summary>
    /// <remarks>
    /// A clip that imports without a hash is worse than one that does not import at all only if
    /// the hash were essential - it is not. Detection simply cannot speak for that clip until the
    /// sanitizer backfills it.
    /// </remarks>
    /// <param name="filePath">The file to hash.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The quick hash, or null when it could not be computed.</returns>
    private async Task<string?> TryComputeQuickHashAsync(
        string filePath, CancellationToken cancellationToken)
    {
        try
        {
            return await _fileHashes.ComputeQuickHashAsync(filePath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not hash {FilePath}; importing without one.", filePath);
            return null;
        }
    }

    /// <summary>
    /// Finds a clip already in the library with the same contents as the given file.
    /// </summary>
    /// <remarks>
    /// The quick hash only screens, so every candidate it returns is confirmed by comparing full
    /// hashes before the file is called a duplicate. A candidate whose own file has since gone
    /// missing cannot be confirmed and is passed over, so a stale row never blocks an import.
    /// </remarks>
    /// <param name="filePath">The file being imported.</param>
    /// <param name="quickHash">Its quick hash.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The matching clip, or null when none matches.</returns>
    private async Task<Clip?> FindDuplicateAsync(
        string filePath, string quickHash, CancellationToken cancellationToken)
    {
        var candidates = await _clips.GetByFileHashAsync(quickHash, cancellationToken);
        if (candidates.Count == 0) return null;

        string? fullHash = null;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_fileSystem.FileExists(candidate.FilePath)) continue;

            // Computed once, and only when there is a candidate worth confirming.
            fullHash ??= await TryComputeFullHashAsync(filePath, cancellationToken);
            if (fullHash is null) return null;

            var candidateHash = await TryComputeFullHashAsync(candidate.FilePath, cancellationToken);
            if (candidateHash == fullHash) return candidate;

            _logger.LogInformation(
                "Quick hash collision between {FilePath} and clip {ClipId}; contents differ.",
                filePath, candidate.Id);
        }

        return null;
    }

    /// <summary>Hashes a file in full, returning null when it cannot be read.</summary>
    /// <param name="filePath">The file to hash.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The full hash, or null when it could not be computed.</returns>
    private async Task<string?> TryComputeFullHashAsync(
        string filePath, CancellationToken cancellationToken)
    {
        try
        {
            return await _fileHashes.ComputeFullHashAsync(filePath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not hash {FilePath} to confirm a duplicate.", filePath);
            return null;
        }
    }
}
