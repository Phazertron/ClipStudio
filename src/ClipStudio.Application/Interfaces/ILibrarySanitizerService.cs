namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Repairs the media-cache by regenerating any missing thumbnail or preview-strip files
/// and removing orphaned cache files that are no longer referenced by any clip in the database.
/// </summary>
/// <remarks>
/// This is the expensive half of what used to run on every launch. It now runs only when the user
/// asks for a repair; the cheap per-launch check lives in <see cref="ILibraryHealthCheckService"/>.
/// </remarks>
public interface ILibrarySanitizerService
{
    /// <summary>
    /// Runs the full sanitize pass.
    /// <list type="bullet">
    ///   <item>For each clip that is missing its thumbnail or preview strip on disk, regenerates the file
    ///   and persists the updated path back to the database.</item>
    ///   <item>Enumerates the media-cache directory and deletes any file not referenced by a current clip.</item>
    /// </list>
    /// </summary>
    /// <param name="progress">Optional progress sink; receives a human-readable status message after each step.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The summary of what the pass did.</returns>
    /// <remarks>
    /// The summary is returned as well as reported through <paramref name="progress"/>, because a
    /// caller that wants the outcome cannot reliably scrape it from the progress sink:
    /// <see cref="Progress{T}"/> posts asynchronously, so the last callback can still be queued
    /// when this task completes.
    /// </remarks>
    Task<string> SanitizeAsync(IProgress<string>? progress = null, CancellationToken ct = default);
}
