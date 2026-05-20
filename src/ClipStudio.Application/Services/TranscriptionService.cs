using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;
using FFMpegCore;
using Microsoft.Extensions.Logging;
using Whisper.net;

namespace ClipStudio.Application.Services;

/// <summary>
/// Implements <see cref="ITranscriptionService"/> using the local Whisper.net inference engine.
/// The pipeline is: extract the selected audio track(s) to a temporary 16 kHz mono WAV via FFmpeg
/// (mixing multiple tracks when needed), run Whisper to obtain timed text segments, write an SRT
/// file, and persist the result to the database via <see cref="ITranscriptionRepository"/>.
/// </summary>
public sealed class TranscriptionService : ITranscriptionService
{
    private readonly IClipRepository _clips;
    private readonly ITranscriptionRepository _transcriptions;
    private readonly ISettingsService _settings;
    private readonly ILogger<TranscriptionService> _logger;

    /// <summary>Initializes a new instance of <see cref="TranscriptionService"/>.</summary>
    public TranscriptionService(
        IClipRepository clips,
        ITranscriptionRepository transcriptions,
        ISettingsService settings,
        ILogger<TranscriptionService> logger)
    {
        _clips          = clips;
        _transcriptions = transcriptions;
        _settings       = settings;
        _logger         = logger;
    }

    /// <inheritdoc/>
    public async Task<Transcription> TranscribeAsync(
        int clipId,
        IReadOnlyList<int> ffmpegTrackIndices,
        string modelPath,
        TranscriptionBackend backend,
        string language,
        IProgress<float>? progress,
        CancellationToken cancellationToken = default)
    {
        if (ffmpegTrackIndices is null || ffmpegTrackIndices.Count == 0)
            throw new ArgumentException("At least one track index must be supplied.", nameof(ffmpegTrackIndices));

        var clip = await _clips.GetByIdAsync(clipId, cancellationToken)
            ?? throw new ArgumentException($"Clip {clipId} not found.", nameof(clipId));

        if (!File.Exists(modelPath))
            throw new FileNotFoundException("Whisper model file not found.", modelPath);

        var tempWav = Path.ChangeExtension(Path.GetTempFileName(), ".wav");
        try
        {
            await ExtractAudioAsync(clip.FilePath, ffmpegTrackIndices, tempWav, cancellationToken);
            return await RunPipelineAsync(clipId, clip.FilePath, clip.FileName, tempWav, modelPath, backend, language, progress, cancellationToken);
        }
        finally
        {
            TryDeleteFile(tempWav);
        }
    }

    /// <inheritdoc/>
    public async Task<Transcription> TranscribeFromFileAsync(
        int clipId,
        string audioFilePath,
        string modelPath,
        TranscriptionBackend backend,
        string language,
        IProgress<float>? progress,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(audioFilePath))
            throw new FileNotFoundException("Audio file not found.", audioFilePath);

        var clip = await _clips.GetByIdAsync(clipId, cancellationToken)
            ?? throw new ArgumentException($"Clip {clipId} not found.", nameof(clipId));

        if (!File.Exists(modelPath))
            throw new FileNotFoundException("Whisper model file not found.", modelPath);

        var tempWav = Path.ChangeExtension(Path.GetTempFileName(), ".wav");
        try
        {
            // Extract the default audio stream from the supplied file (no track selection).
            await FFMpegArguments
                .FromFileInput(audioFilePath, verifyExists: true)
                .OutputToFile(tempWav, overwrite: true, options => options
                    .WithCustomArgument("-vn -ac 1 -ar 16000 -f wav -acodec pcm_s16le"))
                .CancellableThrough(cancellationToken)
                .ProcessAsynchronously(throwOnError: true);

            return await RunPipelineAsync(clipId, clip.FilePath, clip.FileName, tempWav, modelPath, backend, language, progress, cancellationToken);
        }
        finally
        {
            TryDeleteFile(tempWav);
        }
    }

    // ---- Private helpers ----

    /// <summary>
    /// Extracts one or more audio tracks from the source video as a single 16 kHz mono PCM WAV.
    /// When multiple indices are supplied the tracks are blended with FFmpeg amix.
    /// </summary>
    private static async Task ExtractAudioAsync(
        string videoPath,
        IReadOnlyList<int> trackIndices,
        string outputWav,
        CancellationToken cancellationToken)
    {
        string audioArgs;

        if (trackIndices.Count == 1)
        {
            // Single-track: simple map.
            audioArgs = $"-map 0:a:{trackIndices[0]} -ac 1 -ar 16000 -vn -f wav -acodec pcm_s16le";
        }
        else
        {
            // Multi-track: build amix filter graph.
            // [0:a:0][0:a:1]amix=inputs=2:duration=first:normalize=0
            var inputs     = string.Concat(trackIndices.Select(i => $"[0:a:{i}]"));
            var inputCount = trackIndices.Count;
            audioArgs = $"-filter_complex \"{inputs}amix=inputs={inputCount}:duration=first:normalize=0\" "
                      + $"-ac 1 -ar 16000 -vn -f wav -acodec pcm_s16le";
        }

        await FFMpegArguments
            .FromFileInput(videoPath, verifyExists: true)
            .OutputToFile(outputWav, overwrite: true, options => options
                .WithCustomArgument(audioArgs))
            .CancellableThrough(cancellationToken)
            .ProcessAsynchronously(throwOnError: true);
    }

    /// <summary>
    /// Runs Whisper inference on the WAV file then writes the SRT and persists to the DB.
    /// Shared by both <see cref="TranscribeAsync"/> and <see cref="TranscribeFromFileAsync"/>.
    /// </summary>
    private async Task<Transcription> RunPipelineAsync(
        int clipId,
        string clipFilePath,
        string clipFileName,
        string wavPath,
        string modelPath,
        TranscriptionBackend backend,
        string language,
        IProgress<float>? progress,
        CancellationToken cancellationToken)
    {
        var segments = await RunWhisperAsync(wavPath, modelPath, backend, language, progress, cancellationToken);
        var srtPath  = BuildSrtPath(clipFilePath, clipFileName);
        await SrtWriter.WriteToFileAsync(segments, srtPath, cancellationToken);

        var modelName    = Path.GetFileNameWithoutExtension(modelPath);
        var transcription = new Transcription
        {
            ClipId      = clipId,
            CreatedAt   = DateTime.UtcNow,
            Language    = language,
            ModelName   = modelName,
            SrtFilePath = srtPath,
            Segments    = segments
        };

        await _transcriptions.AddAsync(transcription, cancellationToken);
        _logger.LogInformation(
            "Transcription complete for clip {ClipId}: {SegmentCount} segments, SRT at {SrtPath}",
            clipId, segments.Count, srtPath);

        return transcription;
    }

    /// <summary>
    /// Runs Whisper.net on the given WAV file and returns an ordered list of
    /// <see cref="TranscriptionSegment"/> instances ready to be persisted.
    /// </summary>
    private static async Task<List<TranscriptionSegment>> RunWhisperAsync(
        string wavPath,
        string modelPath,
        TranscriptionBackend backend,
        string language,
        IProgress<float>? progress,
        CancellationToken cancellationToken)
    {
        var useGpu  = backend != TranscriptionBackend.Cpu;
        var factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = useGpu });
        var builder = factory.CreateBuilder();

        // Always pass the language so whisper.cpp does not silently fall back to translation mode.
        var langCode = string.IsNullOrWhiteSpace(language) ? "auto" : language.Trim().ToLowerInvariant();
        builder = builder.WithLanguage(langCode);

        using var processor = builder.Build();

        var segments = new List<TranscriptionSegment>();
        var index    = 1;

        await using var stream = File.OpenRead(wavPath);

        await foreach (var seg in processor.ProcessAsync(stream, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            segments.Add(new TranscriptionSegment
            {
                IndexNumber = index++,
                StartMs     = (long)seg.Start.TotalMilliseconds,
                EndMs       = (long)seg.End.TotalMilliseconds,
                Text        = seg.Text
            });

            // Approximate progress: use end-time heuristic (no total-duration available without an extra FFProbe call).
            progress?.Report(Math.Min(1f, (float)(seg.End.TotalSeconds / 3600)));
        }

        progress?.Report(1f);
        return segments;
    }

    /// <summary>
    /// Builds the output SRT file path, placing it in the configured SRT folder when set,
    /// or beside the clip file when not.
    /// </summary>
    private string BuildSrtPath(string clipFilePath, string clipFileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(clipFileName) + ".srt";
        var folder   = _settings.Current.TranscriptionSrtFolder;

        if (!string.IsNullOrWhiteSpace(folder))
            return Path.Combine(folder, baseName);

        var clipDir = Path.GetDirectoryName(clipFilePath);
        return Path.Combine(clipDir ?? string.Empty, baseName);
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { /* non-fatal */ }
    }
}
