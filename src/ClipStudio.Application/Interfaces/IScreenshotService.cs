using ClipStudio.Core.Entities;

namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Provides operations for capturing and managing still-image screenshots from clips.
/// </summary>
public interface IScreenshotService
{
    /// <summary>
    /// Captures the frame at the given playback position in the clip, saves it as PNG,
    /// and records a <see cref="Screenshot"/> entity in the library database.
    /// </summary>
    /// <param name="clipId">The identifier of the clip to capture from.</param>
    /// <param name="timestamp">The playback position at which to capture the frame.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The persisted <see cref="Screenshot"/> entity.</returns>
    Task<Screenshot> CaptureAsync(int clipId, TimeSpan timestamp, CancellationToken cancellationToken = default);

    /// <summary>Returns all screenshots associated with the given clip, ordered by timestamp.</summary>
    Task<IReadOnlyList<Screenshot>> GetByClipAsync(int clipId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a screenshot record and removes the image file from disk.</summary>
    Task DeleteAsync(int screenshotId, CancellationToken cancellationToken = default);
}
