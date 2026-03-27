using System.Globalization;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using FFMpegCore;
using Microsoft.Extensions.Logging;

namespace ClipStudio.Application.Services;

/// <summary>
/// Video processing service backed by FFmpeg via FFMpegCore.
/// Handles metadata extraction, thumbnail generation, preview strip creation,
/// screenshot capture, and clip trimming.
/// </summary>
public sealed class MediaService : IMediaService
{
    private readonly ILogger<MediaService> _logger;

    /// <summary>Initializes a new instance of <see cref="MediaService"/>.</summary>
    public MediaService(ILogger<MediaService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<MediaMetadata> GetMetadataAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var analysis = await FFProbe.AnalyseAsync(filePath, cancellationToken: cancellationToken);
        var video = analysis.VideoStreams.FirstOrDefault();

        // Parse the embedded creation_time tag from the format tags (e.g., from OBS mkv/mp4 containers).
        DateTime? embeddedCreationTime = null;
        if (analysis.Format?.Tags is not null
            && analysis.Format.Tags.TryGetValue("creation_time", out var creationTimeStr)
            && DateTime.TryParse(creationTimeStr,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsedTime))
        {
            embeddedCreationTime = parsedTime.Kind == DateTimeKind.Utc
                ? parsedTime
                : parsedTime.ToUniversalTime();
        }

        return new MediaMetadata
        {
            Duration = analysis.Duration,
            Width = video?.Width ?? 0,
            Height = video?.Height ?? 0,
            Codec = video?.CodecName ?? string.Empty,
            FileSizeBytes = new FileInfo(filePath).Length,
            EmbeddedCreationTime = embeddedCreationTime,
            AudioStreamCount = analysis.AudioStreams.Count
        };
    }

    /// <inheritdoc/>
    public async Task<string> GenerateThumbnailAsync(
        string filePath,
        string outputDirectory,
        TimeSpan offset,
        string? filenameSuffix = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var suffix     = filenameSuffix is { Length: > 0 } s ? $"_{s}" : string.Empty;
        var outputPath = Path.Combine(outputDirectory, $"{FileId(filePath)}{suffix}_thumb.png");

        await FFMpeg.SnapshotAsync(filePath, outputPath, captureTime: offset);
        _logger.LogDebug("Thumbnail generated: {Path}", outputPath);
        return outputPath;
    }

    /// <inheritdoc/>
    public async Task<string> GeneratePreviewStripAsync(
        string filePath,
        string outputDirectory,
        int frameCount,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, $"{FileId(filePath)}_strip.jpg");

        // Build a single horizontal sprite sheet using FFmpeg's tile filter.
        // Compute the fps required to produce exactly frameCount frames over the clip duration.
        // Using a floating-point rate avoids the integer-division bug that left most tiles black
        // for short clips (e.g. 2-second clip with 20 requested frames).
        var analysis     = await FFProbe.AnalyseAsync(filePath, cancellationToken: cancellationToken);
        var totalSeconds = Math.Max(analysis.Duration.TotalSeconds, 1.0);
        var fps          = Math.Clamp(frameCount / totalSeconds, 0.01, 30.0);
        var fpsStr       = fps.ToString("F4", CultureInfo.InvariantCulture);

        // tile filter produces a single composite frame; -update 1 tells the image2 muxer
        // to write one file instead of expecting an image-sequence pattern.
        // -frames:v 1 stops after the first complete tile so extra frames don't overwrite the file.
        await FFMpegArguments
            .FromFileInput(filePath)
            .OutputToFile(outputPath, overwrite: true, options => options
                .WithCustomArgument($"-vf fps={fpsStr},scale=160:90,tile={frameCount}x1 -frames:v 1 -update 1")
                .ForceFormat("image2"))
            .ProcessAsynchronously();

        _logger.LogDebug("Preview strip generated: {Path}", outputPath);
        return outputPath;
    }

    /// <inheritdoc/>
    public async Task<string> CaptureScreenshotAsync(
        string filePath,
        string outputDirectory,
        TimeSpan timestamp,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(
            outputDirectory,
            $"screenshot_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.png");

        await FFMpeg.SnapshotAsync(filePath, outputPath, captureTime: timestamp);
        _logger.LogInformation("Screenshot captured: {Path}", outputPath);
        return outputPath;
    }

    /// <inheritdoc/>
    public async Task TrimAsync(
        string inputPath,
        string outputPath,
        TimeSpan startTime,
        TimeSpan endTime,
        CancellationToken cancellationToken = default)
    {
        var duration = endTime - startTime;
        if (duration <= TimeSpan.Zero)
            throw new ArgumentException("End time must be after start time.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        // Stream-copy all tracks with no re-encode (passthrough all original audio tracks).
        await FFMpegArguments
            .FromFileInput(inputPath, verifyExists: true, options => options
                .Seek(startTime))
            .OutputToFile(outputPath, overwrite: true, options => options
                .WithDuration(duration)
                .CopyChannel())
            .ProcessAsynchronously(throwOnError: true);

        _logger.LogInformation("Trimmed clip written to: {Path}", outputPath);
    }

    private static string FileId(string filePath)
        => Path.GetFileNameWithoutExtension(filePath)
               .Replace(" ", "_")
               .Replace("[", "")
               .Replace("]", "");
}
