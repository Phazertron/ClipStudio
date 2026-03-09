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
    private readonly ILogger<ImportService> _logger;

    /// <summary>Initializes a new instance of <see cref="ImportService"/>.</summary>
    public ImportService(
        IClipRepository clips,
        ISourceFolderRepository folders,
        IMediaService media,
        ISettingsService settings,
        IPlayerRepository players,
        IGameTagAliasService gameAliases,
        ILogger<ImportService> logger)
    {
        _clips = clips;
        _folders = folders;
        _media = media;
        _settings = settings;
        _players = players;
        _gameAliases = gameAliases;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ImportResult> ImportFileAsync(
        string filePath,
        int sourceFolderId,
        CancellationToken cancellationToken = default)
    {
        if (!ClipFileNameParser.IsSupportedVideoFile(filePath))
            return ImportResult.Failed($"Unsupported file type: {Path.GetExtension(filePath)}");

        if (!File.Exists(filePath))
            return ImportResult.Failed($"File not found: {filePath}");

        if (await _clips.ExistsByFilePathAsync(filePath, cancellationToken))
            return ImportResult.Skipped($"Already in library: {Path.GetFileName(filePath)}");

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
            var recordedAt = ClipFileNameParser.ExtractTimestamp(filePath)
                             ?? File.GetCreationTimeUtc(filePath);

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
                SuggestedGameName = suggestedGameName
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

        if (!Directory.Exists(folder.Path))
        {
            _logger.LogWarning("Source folder path does not exist: {Path}", folder.Path);
            return [];
        }

        var files = Directory.EnumerateFiles(folder.Path)
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
            var result = await ImportFileAsync(file, sourceFolderId, cancellationToken);
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

    private static string GetDataDirectory()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClipStudio",
            "media-cache");

        Directory.CreateDirectory(dir);
        return dir;
    }
}
