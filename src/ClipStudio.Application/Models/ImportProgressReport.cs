namespace ClipStudio.Application.Models;

/// <summary>
/// Carries progress information about a folder scan that is reported back to the caller
/// after each file is processed.
/// </summary>
/// <param name="TotalFiles">Total number of video files discovered in the folder.</param>
/// <param name="CurrentFileIndex">1-based index of the file just processed (0 before any file is processed).</param>
/// <param name="CurrentFileName">File name (not full path) currently being processed.</param>
/// <param name="Imported">Number of files successfully imported so far.</param>
/// <param name="Skipped">Number of files skipped because they are already in the library.</param>
/// <param name="Failed">Number of files that failed to import.</param>
public sealed record ImportProgressReport(
    int TotalFiles,
    int CurrentFileIndex,
    string CurrentFileName,
    int Imported,
    int Skipped,
    int Failed);
