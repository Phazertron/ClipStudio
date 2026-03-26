using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;

namespace ClipStudio.Core.Interfaces;

/// <summary>
/// Defines the contract for the local speech-to-text transcription pipeline.
/// Implementations extract a designated audio track from a clip, run it through a
/// local Whisper model, write an SRT subtitle file, and persist the results to the repository.
/// </summary>
public interface ITranscriptionService
{
    /// <summary>
    /// Transcribes the specified audio track of a clip using a local Whisper model.
    /// </summary>
    /// <param name="clipId">The identifier of the clip to transcribe.</param>
    /// <param name="ffmpegTrackIndex">
    /// The 0-based FFmpeg audio stream index to extract (matches
    /// <c>AudioTrackViewModel.FfmpegStreamIndex</c> in the UI layer).
    /// </param>
    /// <param name="modelPath">Absolute path to the GGML Whisper model file (.bin).</param>
    /// <param name="backend">The hardware inference backend to use.</param>
    /// <param name="language">
    /// BCP-47 language code (e.g. "en", "fr") or "auto" for automatic detection.
    /// </param>
    /// <param name="progress">
    /// Optional progress reporter; receives values in the range [0, 1] as inference advances.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The persisted <see cref="Transcription"/> with its segments populated.</returns>
    Task<Transcription> TranscribeAsync(
        int clipId,
        int ffmpegTrackIndex,
        string modelPath,
        TranscriptionBackend backend,
        string language,
        IProgress<float>? progress,
        CancellationToken cancellationToken = default);
}
