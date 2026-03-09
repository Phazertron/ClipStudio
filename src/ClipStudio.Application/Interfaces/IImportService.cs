using ClipStudio.Application.Models;

namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Orchestrates the import pipeline for a video file: metadata extraction, thumbnail generation,
/// filename parsing, and persistence to the library database.
/// </summary>
public interface IImportService
{
    /// <summary>
    /// Imports a single video file into the library under the given source folder.
    /// If a clip with the same file path already exists, the import is skipped.
    /// </summary>
    /// <param name="filePath">The absolute path to the video file to import.</param>
    /// <param name="sourceFolderId">The identifier of the source folder this file belongs to.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>An <see cref="ImportResult"/> describing the outcome.</returns>
    Task<ImportResult> ImportFileAsync(
        string filePath,
        int sourceFolderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans a source folder and imports all unrecognised video files found within it.
    /// </summary>
    /// <param name="sourceFolderId">The identifier of the source folder to scan.</param>
    /// <param name="progress">
    /// Optional progress sink that receives an <see cref="ImportProgressReport"/> after each
    /// file is processed, allowing the UI to display real-time progress information.
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A list of <see cref="ImportResult"/> instances, one per discovered file.</returns>
    Task<IReadOnlyList<ImportResult>> ScanFolderAsync(
        int sourceFolderId,
        IProgress<ImportProgressReport>? progress = null,
        CancellationToken cancellationToken = default);
}
