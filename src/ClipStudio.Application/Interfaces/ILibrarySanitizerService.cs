namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Repairs the media-cache by regenerating any missing thumbnail or preview-strip files
/// and removing orphaned cache files that are no longer referenced by any clip in the database.
/// </summary>
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
    Task SanitizeAsync(IProgress<string>? progress = null, CancellationToken ct = default);
}
