using System;
using System.Collections.Generic;
using System.IO;
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
/// The pipeline is: extract the selected audio track to a temporary 16 kHz mono WAV via FFmpeg,
/// run Whisper to obtain timed text segments, write an SRT file, and persist the result to the
/// database via <see cref="ITranscriptionRepository"/>.
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
        int ffmpegTrackIndex,
        string modelPath,
        TranscriptionBackend backend,
        string language,
        IProgress<float>? progress,
        CancellationToken cancellationToken = default)
    {
        var clip = await _clips.GetByIdAsync(clipId, cancellationToken)
            ?? throw new ArgumentException($"Clip {clipId} not found.", nameof(clipId));

        if (!File.Exists(modelPath))
            throw new FileNotFoundException("Whisper model file not found.", modelPath);

        var tempWav = Path.ChangeExtension(Path.GetTempFileName(), ".wav");
        try
        {
            // Step 1: extract selected track to 16 kHz mono WAV.
            await ExtractAudioTrackAsync(clip.FilePath, ffmpegTrackIndex, tempWav, cancellationToken);

            // Step 2: run Whisper inference.
            var segments = await RunWhisperAsync(tempWav, modelPath, backend, language, progress, cancellationToken);

            // Step 3: determine SRT output path.
            var srtPath = BuildSrtPath(clip.FilePath, clip.FileName);

            // Step 4: write SRT file.
            await SrtWriter.WriteToFileAsync(segments, srtPath, cancellationToken);

            // Step 5: persist to DB.
            var modelName = Path.GetFileNameWithoutExtension(modelPath);
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
            _logger.LogInformation("Transcription complete for clip {ClipId}: {SegmentCount} segments, SRT at {SrtPath}",
                clipId, segments.Count, srtPath);

            return transcription;
        }
        finally
        {
            try { File.Delete(tempWav); } catch { /* non-fatal */ }
        }
    }

    /// <summary>
    /// Uses FFMpegCore to extract a single audio track from the source video as a
    /// 16 kHz mono PCM WAV, which is the format expected by Whisper.
    /// </summary>
    private static async Task ExtractAudioTrackAsync(
        string videoPath,
        int ffmpegTrackIndex,
        string outputWav,
        CancellationToken cancellationToken)
    {
        await FFMpegArguments
            .FromFileInput(videoPath, verifyExists: true)
            .OutputToFile(outputWav, overwrite: true, options => options
                .WithCustomArgument($"-map 0:a:{ffmpegTrackIndex} -ac 1 -ar 16000 -vn -f wav -acodec pcm_s16le"))
            .CancellableThrough(cancellationToken)
            .ProcessAsynchronously(throwOnError: true);
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
        var useGpu = backend != TranscriptionBackend.Cpu;

        var factory   = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = useGpu });
        var builder   = factory.CreateBuilder();

        if (!string.IsNullOrWhiteSpace(language) && language != "auto")
            builder = builder.WithLanguage(language);

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

            // Report approximate progress based on end-time of the latest segment.
            // The segment stream flows in chronological order; we use a rough heuristic
            // since total duration is not available here without an extra FFProbe call.
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
}
