namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Produces a clip's cached images on demand, generating any that are missing.
/// </summary>
/// <remarks>
/// This exists because the startup sanitize used to regenerate missing thumbnails and strips
/// silently on every launch, so a user who never ran Repair Library never noticed one had gone.
/// Moving that work out of startup would have traded a slow start for visibly broken tiles, so it
/// moved here instead: whatever displays an image asks for it, and it is made if it is not there.
/// <para>
/// Every method returns the existing path unchanged when the file is present, so the common case
/// costs one existence check. Concurrent requests for the same asset share one generation - a grid
/// scrolling past twenty cards must not start twenty FFmpeg processes for the same clip.
/// </para>
/// </remarks>
public interface IMediaAssetProvider
{
    /// <summary>
    /// Returns the clip's thumbnail path, generating and persisting it when the file is missing.
    /// </summary>
    /// <param name="clipId">The clip to get a thumbnail for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The path to a thumbnail that exists on disk, or <see langword="null"/> when one could not be
    /// made - the clip is gone, its source file is missing, or FFmpeg failed.
    /// </returns>
    Task<string?> EnsureClipThumbnailAsync(int clipId, CancellationToken ct = default);

    /// <summary>
    /// Returns the clip's preview strip path, generating and persisting it when the file is missing.
    /// </summary>
    /// <param name="clipId">The clip to get a preview strip for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The path to a strip that exists on disk, or <see langword="null"/> when one could not be made.
    /// </returns>
    Task<string?> EnsureClipPreviewStripAsync(int clipId, CancellationToken ct = default);

    /// <summary>
    /// Returns the highlight's thumbnail path, generating and persisting it when the file is missing.
    /// </summary>
    /// <param name="highlightId">The highlight to get a thumbnail for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The path to a thumbnail that exists on disk, or <see langword="null"/> when one could not be made.
    /// </returns>
    Task<string?> EnsureHighlightThumbnailAsync(int highlightId, CancellationToken ct = default);
}
