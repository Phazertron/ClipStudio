using System;
using System.Collections.Generic;
using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ClipStudio.Application.Services;

/// <summary>
/// Manages the lifecycle of video export jobs: creation, sequential processing, and outcome recording.
/// Jobs are persisted to the database so they are visible across different DI scopes.
/// </summary>
public sealed class ExportService : IExportService
{
    private readonly IClipRepository _clips;
    private readonly IHighlightRepository _highlights;
    private readonly IExportJobRepository _exportJobs;
    private readonly IMediaService _media;
    private readonly IClipService _clipService;
    private readonly ILogger<ExportService> _logger;

    /// <summary>Initializes a new instance of <see cref="ExportService"/>.</summary>
    public ExportService(
        IClipRepository clips,
        IHighlightRepository highlights,
        IExportJobRepository exportJobs,
        IMediaService media,
        IClipService clipService,
        ILogger<ExportService> logger)
    {
        _clips = clips;
        _highlights = highlights;
        _exportJobs = exportJobs;
        _media = media;
        _clipService = clipService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ExportJob> QueueAsync(
        int clipId,
        int? highlightId,
        string outputPath,
        TrimMode trimMode,
        bool deleteOriginal,
        TimeSpan? startTime = null,
        TimeSpan? endTime = null,
        CancellationToken cancellationToken = default)
    {
        var clip = await _clips.GetByIdAsync(clipId, cancellationToken)
            ?? throw new InvalidOperationException($"Clip {clipId} not found.");

        Highlight? highlight = null;
        if (highlightId.HasValue)
        {
            highlight = await _highlights.GetByIdAsync(highlightId.Value, cancellationToken)
                ?? throw new InvalidOperationException($"Highlight {highlightId} not found.");
        }

        var job = new ExportJob
        {
            ClipId = clipId,
            Clip = clip,
            HighlightId = highlightId,
            Highlight = highlight,
            OutputPath = outputPath,
            TrimMode = trimMode,
            DeleteOriginalAfterExport = deleteOriginal && trimMode == TrimMode.Destructive,
            StartTime = startTime,
            EndTime = endTime,
            Status = ExportJobStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        await _exportJobs.AddAsync(job, cancellationToken);
        _logger.LogInformation("Export job queued for clip {ClipId}, highlight {HighlightId}.", clipId, highlightId);
        return job;
    }

    /// <inheritdoc/>
    public async Task ProcessQueueAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _exportJobs.GetPendingAsync(cancellationToken);
        _logger.LogInformation("Processing {Count} pending export job(s).", pending.Count);

        foreach (var job in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessJobAsync(job, cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ExportJob>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _exportJobs.GetAllAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ExportJob>> GetJobsByClipAsync(int clipId, CancellationToken cancellationToken = default)
    {
        return await _exportJobs.GetByClipAsync(clipId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task CancelJobAsync(int jobId, CancellationToken cancellationToken = default)
    {
        var job = await _exportJobs.GetByIdAsync(jobId, cancellationToken);
        if (job is null || job.Status != ExportJobStatus.Pending)
            return;

        job.Status = ExportJobStatus.Cancelled;
        job.CompletedAt = DateTime.UtcNow;
        await _exportJobs.UpdateAsync(job, cancellationToken);
        _logger.LogInformation("Export job {JobId} cancelled by user.", jobId);
    }

    private async Task ProcessJobAsync(ExportJob job, CancellationToken cancellationToken)
    {
        job.Status = ExportJobStatus.Processing;
        await _exportJobs.UpdateAsync(job, cancellationToken);

        try
        {
            var clip = await _clips.GetByIdAsync(job.ClipId, cancellationToken)!;
            TimeSpan start, end;

            if (job.Highlight is not null)
            {
                start = job.Highlight.StartTime;
                end   = job.Highlight.EndTime;
            }
            else if (job.StartTime.HasValue && job.EndTime.HasValue)
            {
                start = job.StartTime.Value;
                end   = job.EndTime.Value;
            }
            else
            {
                start = TimeSpan.Zero;
                end   = clip!.Duration;
            }

            await _media.TrimAsync(clip!.FilePath, job.OutputPath, start, end, cancellationToken);

            if (job.DeleteOriginalAfterExport)
            {
                await _clipService.TrashAsync(job.ClipId, cancellationToken);
                _logger.LogWarning("Original clip {ClipId} moved to trash after destructive export.", job.ClipId);
            }

            job.Status = ExportJobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            await _exportJobs.UpdateAsync(job, cancellationToken);
            _logger.LogInformation("Export job completed: {OutputPath}", job.OutputPath);
        }
        catch (OperationCanceledException)
        {
            job.Status = ExportJobStatus.Cancelled;
            await _exportJobs.UpdateAsync(job, cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            job.Status = ExportJobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.CompletedAt = DateTime.UtcNow;
            await _exportJobs.UpdateAsync(job, cancellationToken);
            _logger.LogError(ex, "Export job failed for clip {ClipId}.", job.ClipId);
        }
    }
}
