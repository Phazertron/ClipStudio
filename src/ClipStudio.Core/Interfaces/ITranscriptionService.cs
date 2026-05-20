using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;

namespace ClipStudio.Core.Interfaces;

/// <summary>
/// Defines the contract for the local speech-to-text transcription pipeline.
/// Implementations extract one or more audio tracks from a clip, optionally mix them,
/// run the result through a local Whisper model, write an SRT subtitle file, and persist
/// the result to the repository.
/// </summary>
public interface ITranscriptionService
{
    /// <summary>
    /// Transcribes one or more audio tracks of a clip using a local Whisper model.
    /// When more than one track index is supplied the tracks are mixed via FFmpeg amix
    /// before inference, producing a single combined transcription.
    /// </summary>
    /// <param name="clipId">The identifier of the clip to transcribe.</param>
    /// <param name="ffmpegTrackIndices">
    /// One or more 0-based FFmpeg audio stream indices to include (matches
    /// <c>AudioTrackViewModel.FfmpegStreamIndex</c> in the UI layer).
    /// Pass a single index for single-track extraction; pass multiple to mix.
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
        IReadOnlyList<int> ffmpegTrackIndices,
        string modelPath,
        TranscriptionBackend backend,
        string language,
        IProgress<float>? progress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribes a pre-mixed audio or video file directly (e.g. an existing audio mix MKV).
    /// The file's default audio stream is extracted to WAV and fed to Whisper without any
    /// additional track selection.  Use this to transcribe the output of <c>GenerateRemuxAsync</c>.
    /// </summary>
    /// <param name="clipId">The identifier of the clip this transcription belongs to.</param>
    /// <param name="audioFilePath">Absolute path to the audio or video file to transcribe.</param>
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
    Task<Transcription> TranscribeFromFileAsync(
        int clipId,
        string audioFilePath,
        string modelPath,
        TranscriptionBackend backend,
        string language,
        IProgress<float>? progress,
        CancellationToken cancellationToken = default);
}
