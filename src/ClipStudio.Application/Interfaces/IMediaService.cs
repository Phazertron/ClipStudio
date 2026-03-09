using System.Threading;
using ClipStudio.Application.Models;

namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Provides video processing operations backed by FFmpeg, including metadata extraction,
/// thumbnail generation, preview strip creation, screenshot capture, and clip trimming.
/// </summary>
public interface IMediaService
{
    /// <summary>Extracts technical metadata from a video file using FFprobe.</summary>
    /// <param name="filePath">The absolute path to the video file.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A <see cref="MediaMetadata"/> instance populated with the file's properties.</returns>
    Task<MediaMetadata> GetMetadataAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a single thumbnail image for a clip at the given playback offset.
    /// </summary>
    /// <param name="filePath">The absolute path to the source video file.</param>
    /// <param name="outputDirectory">The directory in which to save the thumbnail PNG.</param>
    /// <param name="offset">The playback position at which to capture the thumbnail frame.</param>
    /// <param name="filenameSuffix">
    /// Optional suffix appended to the generated filename before the extension.
    /// Use this to avoid filename collisions when generating thumbnails at different offsets
    /// from the same source file (e.g. highlight thumbnails).
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The absolute path to the generated thumbnail file.</returns>
    Task<string> GenerateThumbnailAsync(
        string filePath,
        string outputDirectory,
        TimeSpan offset,
        string? filenameSuffix = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a horizontal sprite-sheet preview strip from evenly-spaced frames across the clip.
    /// Used for hover-scrub preview in the library grid view.
    /// </summary>
    /// <param name="filePath">The absolute path to the source video file.</param>
    /// <param name="outputDirectory">The directory in which to save the strip JPEG.</param>
    /// <param name="frameCount">The number of frames to include in the strip.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The absolute path to the generated strip file.</returns>
    Task<string> GeneratePreviewStripAsync(
        string filePath,
        string outputDirectory,
        int frameCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures a single frame from a video at the specified playback timestamp and saves it as PNG.
    /// </summary>
    /// <param name="filePath">The absolute path to the source video file.</param>
    /// <param name="outputDirectory">The directory in which to save the screenshot PNG.</param>
    /// <param name="timestamp">The playback position at which to capture the frame.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The absolute path to the generated screenshot file.</returns>
    Task<string> CaptureScreenshotAsync(
        string filePath,
        string outputDirectory,
        TimeSpan timestamp,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Trims a video file to the specified in/out range using FFmpeg stream-copy.
    /// All original audio tracks are passed through unchanged (no re-encode or mixing).
    /// Per-track volume and include/exclude settings affect real-time playback preview only
    /// and are applied separately via <see cref="IMixedAudioService"/>.
    /// </summary>
    /// <param name="inputPath">The absolute path to the source video file.</param>
    /// <param name="outputPath">The absolute path where the trimmed output file will be written.</param>
    /// <param name="startTime">The start position of the output segment.</param>
    /// <param name="endTime">The end position of the output segment.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task TrimAsync(
        string inputPath,
        string outputPath,
        TimeSpan startTime,
        TimeSpan endTime,
        CancellationToken cancellationToken = default);
}
