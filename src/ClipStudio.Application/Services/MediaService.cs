using System.Diagnostics;
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

    /// <summary>
    /// Decides whether a file has at least the given number of video keyframes.
    /// </summary>
    /// <remarks>
    /// Asks the question the caller actually has - "are there enough?" - rather than counting them
    /// all, and stops reading the moment the answer is yes. That matters because the cost is
    /// proportional to how much of the container index is walked.
    /// <para>
    /// Reads <c>packet=flags</c> and counts the packets marked <c>K</c>. This walks the container
    /// index without decoding anything, which is what the early exit was always meant to do. The
    /// previous form, <c>-skip_frame nokey -show_entries frame=pts_time</c>, went through the
    /// decoder: on a 503 MB clip it measured 1,724 ms against 236 ms here, and on ffmpeg 4.x it
    /// reported zero keyframes for every file - so every import on Ubuntu 22.04 silently fell
    /// through to the full-decode path and lost the optimisation entirely.
    /// </para>
    /// <para>
    /// Returns false when the count cannot be established, which sends the caller down the safe
    /// full-decode path.
    /// </para>
    /// </remarks>
    /// <param name="filePath">The file to inspect.</param>
    /// <param name="needed">How many keyframes the caller needs.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>Whether at least <paramref name="needed"/> keyframes were found, and how many were seen.</returns>
    private async Task<(bool Enough, int Seen)> HasAtLeastKeyframesAsync(
        string filePath, int needed, CancellationToken cancellationToken)
    {
        try
        {
            // FFMpegCore's own resolution, rather than composing the path by hand: it applies the
            // configured binary folder and the platform's executable extension, and getting either
            // wrong here fails silently into the slow path rather than erroring.
            var exe = GlobalFFOptions.GetFFProbeBinaryPath();

            var startInfo = new ProcessStartInfo
            {
                FileName               = exe,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };

            foreach (var arg in new[]
                     {
                         "-v", "error",
                         "-select_streams", "v:0",
                         "-show_entries", "packet=flags",
                         "-of", "csv=p=0",
                         filePath,
                     })
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = Process.Start(startInfo);
            if (process is null) return (false, 0);

            var seen = 0;
            try
            {
                while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
                {
                    // One line per packet, holding just the flags field. A keyframe packet is
                    // marked 'K' (as in "K__"); everything else is skipped without being counted.
                    if (!line.Contains('K')) continue;

                    if (++seen >= needed)
                        return (true, seen);
                }
            }
            finally
            {
                // Either the answer arrived early or the file ran out. Kill covers the first case;
                // it is harmless in the second, where the process has already exited.
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch { /* it exited between the check and the kill */ }
                }
            }

            // The whole index was read without reaching the target.
            return (false, seen);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not count keyframes for '{Path}'; decoding in full.", filePath);
            return (false, 0);
        }
    }

    /// <inheritdoc/>
    public async Task<string> GeneratePreviewStripAsync(
        string filePath,
        string outputDirectory,
        int frameCount,
        TimeSpan? knownDuration = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, $"{FileId(filePath)}_strip.jpg");

        // Build a single horizontal sprite sheet using FFmpeg's tile filter.
        // Compute the fps required to produce exactly frameCount frames over the clip duration.
        // Using a floating-point rate avoids the integer-division bug that left most tiles black
        // for short clips (e.g. 2-second clip with 20 requested frames).
        var duration = knownDuration
            ?? (await FFProbe.AnalyseAsync(filePath, cancellationToken: cancellationToken)).Duration;

        var totalSeconds = Math.Max(duration.TotalSeconds, 1.0);
        var fps          = Math.Clamp(frameCount / totalSeconds, 0.01, 30.0);
        var fpsStr       = fps.ToString("F4", CultureInfo.InvariantCulture);

        // Decoding only keyframes turns this from the slowest step of an import into one of the
        // fastest - measured at 6.1s versus 0.28s on a 180s clip, because the fps filter drops
        // frames but the decoder still has to decode all ~10,800 of them first.
        //
        // It is only safe when the file has at least as many keyframes as the strip has tiles.
        // Below that the tile filter pads with repeats: a 12s clip with 2 keyframes produced 2
        // distinct tiles instead of 20. The check reads the container index and stops as soon as
        // it has seen enough, so on a keyframe-dense clip it costs a fraction of a full count.
        var (keyframesOnly, keyframesSeen) = await HasAtLeastKeyframesAsync(
            filePath, frameCount, cancellationToken);

        if (!keyframesOnly)
        {
            _logger.LogDebug(
                "Only {Count} keyframe(s) for {Frames} tiles in '{Path}'; decoding in full.",
                keyframesSeen, frameCount, filePath);
        }

        // tile filter produces a single composite frame; -update 1 tells the image2 muxer
        // to write one file instead of expecting an image-sequence pattern.
        // -frames:v 1 stops after the first complete tile so extra frames don't overwrite the file.
        await FFMpegArguments
            .FromFileInput(filePath, verifyExists: true, options =>
            {
                if (keyframesOnly)
                {
                    // Discarding at the demuxer, so non-keyframe packets are never handed to the
                    // decoder at all. Measurably faster than -skip_frame nokey, which discards
                    // after decoding (0.28s versus 1.25s).
                    options.WithCustomArgument("-discard nokey");
                }
            })
            .OutputToFile(outputPath, overwrite: true, options => options
                .WithCustomArgument($"-vf fps={fpsStr},scale=160:90,tile={frameCount}x1 -frames:v 1 -update 1")
                .ForceFormat("image2"))
            .ProcessAsynchronously();

        _logger.LogDebug(
            "Preview strip generated ({Mode}): {Path}",
            keyframesOnly ? "keyframes only" : "full decode", outputPath);
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
