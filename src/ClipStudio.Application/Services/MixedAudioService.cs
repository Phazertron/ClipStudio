using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using FFMpegCore;
using Microsoft.Extensions.Logging;

namespace ClipStudio.Application.Services;

/// <summary>
/// Generates mixed audio and preview video files from the selected tracks of a video source
/// using FFmpeg. Included tracks are combined with their individual volume multipliers via
/// the amix filter. The remux approach avoids the VLC input-slave pixelation issue on Windows
/// by baking the mixed audio directly into a temporary MKV preview file.
/// </summary>
public sealed class MixedAudioService : IMixedAudioService
{
    private readonly ILogger<MixedAudioService> _logger;

    /// <summary>Initializes a new instance of <see cref="MixedAudioService"/>.</summary>
    public MixedAudioService(ILogger<MixedAudioService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool ShouldUseMix(IEnumerable<TrackMixInfo> tracks)
    {
        var list     = tracks.ToList();
        var included = list.Where(t => t.IsIncluded).ToList();

        if (included.Count == 0) return false;

        // Single track at unity gain — native VLC AudioTrack selection is sufficient.
        if (included.Count == 1 && Math.Abs(included[0].Volume - 1.0) < 0.001) return false;

        // All tracks included at unity gain — VLC default playback, no mix needed.
        if (included.Count == list.Count && list.All(t => Math.Abs(t.Volume - 1.0) < 0.001)) return false;

        return true;
    }

    /// <inheritdoc/>
    public async Task<string> GenerateMixAsync(
        string videoPath,
        IEnumerable<TrackMixInfo> tracks,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var included = tracks.Where(t => t.IsIncluded).ToList();
        if (included.Count == 0)
            throw new InvalidOperationException("At least one audio track must be included in the mix.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        if (included.Count == 1 && Math.Abs(included[0].Volume - 1.0) < 0.001)
        {
            // Fast path: single track at unity gain — map directly, no filter needed.
            await FFMpegArguments
                .FromFileInput(videoPath, verifyExists: true)
                .OutputToFile(outputPath, overwrite: true, options => options
                    .WithCustomArgument($"-map 0:a:{included[0].FfmpegStreamIndex} -vn -c:a pcm_s16le"))
                .CancellableThrough(cancellationToken)
                .ProcessAsynchronously(throwOnError: true);
        }
        else
        {
            // Build amix filter with per-track volume.
            var filter = BuildMixFilter(included);

            await FFMpegArguments
                .FromFileInput(videoPath, verifyExists: true)
                .OutputToFile(outputPath, overwrite: true, options => options
                    .WithCustomArgument($"-filter_complex \"{filter}\" -map \"[mixout]\" -vn -c:a pcm_s16le"))
                .CancellableThrough(cancellationToken)
                .ProcessAsynchronously(throwOnError: true);
        }

        _logger.LogInformation("Audio mix written to: {Path}", outputPath);
        return outputPath;
    }

    /// <inheritdoc/>
    public async Task<string> GenerateRemuxAsync(
        string videoPath,
        IEnumerable<TrackMixInfo> tracks,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var trackList = tracks.ToList();

        // Generate the mixed WAV into a temp file alongside the output.
        var wavPath = Path.ChangeExtension(outputPath, ".wav");
        await GenerateMixAsync(videoPath, trackList, wavPath, cancellationToken);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        // Remux: copy video stream from original, replace audio with the mixed WAV.
        // -map 0:v   = video from original input
        // -map 1:a   = audio from WAV input
        // -c:v copy  = stream-copy video (no re-encode)
        // -c:a aac   = encode to AAC (~90 % smaller than uncompressed pcm_s16le)
        // -b:a 256k  = 256 kbps CBR — transparent quality for stereo gaming audio
        await FFMpegArguments
            .FromFileInput(videoPath, verifyExists: true)
            .AddFileInput(wavPath)
            .OutputToFile(outputPath, overwrite: true, options => options
                .WithCustomArgument("-map 0:v -map 1:a -c:v copy -c:a aac -b:a 256k"))
            .CancellableThrough(cancellationToken)
            .ProcessAsynchronously(throwOnError: true);

        // Clean up the intermediate WAV.
        try { File.Delete(wavPath); } catch { /* non-fatal */ }

        _logger.LogInformation("Remuxed preview written to: {Path}", outputPath);
        return outputPath;
    }

    /// <summary>
    /// Builds an FFmpeg <c>filter_complex</c> string that applies individual volume multipliers and
    /// mixes all included tracks into a single output stream labelled <c>[mixout]</c>.
    /// Uses <see cref="TrackMixInfo.FfmpegStreamIndex"/> (0-based) for correct stream mapping.
    /// </summary>
    private static string BuildMixFilter(IReadOnlyList<TrackMixInfo> included)
    {
        var sb     = new StringBuilder();
        var labels = new List<string>();

        for (var i = 0; i < included.Count; i++)
        {
            var t     = included[i];
            var label = $"[a{i}]";
            sb.Append(CultureInfo.InvariantCulture,
                $"[0:a:{t.FfmpegStreamIndex}]volume={t.Volume:F3}{label};");
            labels.Add(label);
        }

        foreach (var l in labels)
            sb.Append(l);
        sb.Append($"amix=inputs={included.Count}:normalize=0[mixout]");

        return sb.ToString();
    }
}
