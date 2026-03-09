using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClipStudio.Application.Models;

namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Generates a mixed audio file from the audio tracks of a video source using FFmpeg.
/// Used to produce a real-time playback preview that reflects the user's per-track
/// include/volume settings without modifying the source file.
/// </summary>
public interface IMixedAudioService
{
    /// <summary>
    /// Determines whether the given track configuration requires FFmpeg mixing or can be
    /// handled by native VLC audio-track selection (zero latency, no sync drift).
    /// Returns <see langword="false"/> when exactly one track is included at unity gain,
    /// or when all tracks are included at unity gain; returns <see langword="true"/> otherwise.
    /// </summary>
    /// <param name="tracks">The per-track mix parameters to evaluate.</param>
    bool ShouldUseMix(IEnumerable<TrackMixInfo> tracks);

    /// <summary>
    /// Generates a mixed WAV audio file from the specified tracks of <paramref name="videoPath"/>.
    /// Included tracks are combined with their individual volume multipliers via FFmpeg amix.
    /// </summary>
    /// <param name="videoPath">The absolute path to the source video file.</param>
    /// <param name="tracks">
    /// The per-track mix parameters. Tracks where <see cref="TrackMixInfo.IsIncluded"/> is
    /// <see langword="false"/> are excluded from the output entirely.
    /// Use <see cref="TrackMixInfo.FfmpegStreamIndex"/> (not <see cref="TrackMixInfo.TrackIndex"/>)
    /// to pass the correct 0-based FFmpeg stream indices.
    /// </param>
    /// <param name="outputPath">The absolute path to write the output WAV file.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The absolute path of the generated WAV file (same as <paramref name="outputPath"/>).</returns>
    Task<string> GenerateMixAsync(
        string videoPath,
        IEnumerable<TrackMixInfo> tracks,
        string outputPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a preview video file with the video stream copied from
    /// <paramref name="videoPath"/> and the audio replaced by a mixed WAV produced from
    /// the specified <paramref name="tracks"/>. The result can be opened directly by VLC
    /// without an input-slave, eliminating the sync drift and pixelation that input-slave
    /// causes on Windows.
    /// </summary>
    /// <param name="videoPath">The absolute path to the source video file.</param>
    /// <param name="tracks">The per-track mix parameters (must include at least one included track).</param>
    /// <param name="outputPath">The absolute path to write the remuxed preview file (MKV recommended).</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The absolute path of the generated preview file (same as <paramref name="outputPath"/>).</returns>
    Task<string> GenerateRemuxAsync(
        string videoPath,
        IEnumerable<TrackMixInfo> tracks,
        string outputPath,
        CancellationToken cancellationToken = default);
}
