using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Services;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClipStudio.Tests.Application;

/// <summary>
/// Unit tests for <see cref="ExportService"/> covering queueing validation, job-queue ordering,
/// failure isolation, cancellation and the destructive-export trash step.
/// </summary>
public sealed class ExportServiceTests
{
    private readonly Mock<IClipRepository> _clipRepo = new();
    private readonly Mock<IHighlightRepository> _highlightRepo = new();
    private readonly Mock<IExportJobRepository> _jobRepo = new();
    private readonly Mock<IMediaService> _media = new();
    private readonly Mock<IClipService> _clipService = new();
    private readonly ExportService _service;

    /// <summary>Sets up a service whose clip 1 exists and whose media trimming succeeds.</summary>
    public ExportServiceTests()
    {
        _clipRepo
            .Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clip
            {
                Id = 1,
                FilePath = "/clips/Replay.mp4",
                FileName = "Replay.mp4",
                Duration = TimeSpan.FromMinutes(5)
            });

        _service = new ExportService(
            _clipRepo.Object,
            _highlightRepo.Object,
            _jobRepo.Object,
            _media.Object,
            _clipService.Object,
            NullLogger<ExportService>.Instance);
    }

    /// <summary>Builds a pending export job for clip 1.</summary>
    /// <param name="id">The job identifier.</param>
    /// <param name="outputPath">The destination path for the exported file.</param>
    /// <returns>A pending job.</returns>
    private static ExportJob PendingJob(int id, string outputPath) => new()
    {
        Id = id,
        ClipId = 1,
        OutputPath = outputPath,
        TrimMode = TrimMode.NonDestructive,
        Status = ExportJobStatus.Pending,
        CreatedAt = DateTime.UtcNow
    };

    // ---- QueueAsync ----

    [Fact]
    public async Task QueueAsync_UnknownClip_Throws()
    {
        _clipRepo
            .Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clip?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.QueueAsync(
            99, null, "/out/clip.mp4", TrimMode.NonDestructive, false));
    }

    [Fact]
    public async Task QueueAsync_UnknownHighlight_Throws()
    {
        _highlightRepo
            .Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Highlight?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.QueueAsync(
            1, 99, "/out/clip.mp4", TrimMode.NonDestructive, false));
    }

    [Fact]
    public async Task QueueAsync_ValidRequest_PersistsAPendingJob()
    {
        var job = await _service.QueueAsync(
            1, null, "/out/clip.mp4", TrimMode.NonDestructive, false,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20));

        Assert.Equal(ExportJobStatus.Pending, job.Status);
        Assert.Equal("/out/clip.mp4", job.OutputPath);
        Assert.Equal(TimeSpan.FromSeconds(5), job.StartTime);
        Assert.Equal(TimeSpan.FromSeconds(20), job.EndTime);
        _jobRepo.Verify(r => r.AddAsync(job, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QueueAsync_DeleteOriginal_IsIgnoredForNonDestructiveTrims()
    {
        var job = await _service.QueueAsync(
            1, null, "/out/clip.mp4", TrimMode.NonDestructive, deleteOriginal: true);

        Assert.False(job.DeleteOriginalAfterExport);
    }

    [Fact]
    public async Task QueueAsync_DeleteOriginal_IsKeptForDestructiveTrims()
    {
        var job = await _service.QueueAsync(
            1, null, "/out/clip.mp4", TrimMode.Destructive, deleteOriginal: true);

        Assert.True(job.DeleteOriginalAfterExport);
    }

    // ---- ProcessQueueAsync ----

    [Fact]
    public async Task ProcessQueueAsync_ProcessesJobsInQueueOrder()
    {
        var jobs = new[]
        {
            PendingJob(1, "/out/first.mp4"),
            PendingJob(2, "/out/second.mp4"),
            PendingJob(3, "/out/third.mp4")
        };
        _jobRepo.Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>())).ReturnsAsync(jobs);

        var processed = new List<string>();
        _media
            .Setup(m => m.TrimAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, TimeSpan, TimeSpan, CancellationToken>(
                (_, output, _, _, _) => processed.Add(output))
            .Returns(Task.CompletedTask);

        await _service.ProcessQueueAsync();

        Assert.Equal(["/out/first.mp4", "/out/second.mp4", "/out/third.mp4"], processed);
        Assert.All(jobs, j => Assert.Equal(ExportJobStatus.Completed, j.Status));
        Assert.All(jobs, j => Assert.NotNull(j.CompletedAt));
    }

    [Fact]
    public async Task ProcessQueueAsync_FailedJob_IsMarkedAndTheQueueContinues()
    {
        var failing = PendingJob(1, "/out/bad.mp4");
        var healthy = PendingJob(2, "/out/good.mp4");
        _jobRepo
            .Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([failing, healthy]);

        _media
            .Setup(m => m.TrimAsync(
                It.IsAny<string>(), "/out/bad.mp4", It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("codec not supported"));

        await _service.ProcessQueueAsync();

        Assert.Equal(ExportJobStatus.Failed, failing.Status);
        Assert.Equal("codec not supported", failing.ErrorMessage);
        Assert.NotNull(failing.CompletedAt);
        Assert.Equal(ExportJobStatus.Completed, healthy.Status);
    }

    [Fact]
    public async Task ProcessQueueAsync_NoRange_TrimsTheWholeClip()
    {
        _jobRepo
            .Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([PendingJob(1, "/out/clip.mp4")]);

        await _service.ProcessQueueAsync();

        _media.Verify(m => m.TrimAsync(
            "/clips/Replay.mp4", "/out/clip.mp4",
            TimeSpan.Zero, TimeSpan.FromMinutes(5),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessQueueAsync_ExplicitRange_IsUsed()
    {
        var job = PendingJob(1, "/out/clip.mp4");
        job.StartTime = TimeSpan.FromSeconds(30);
        job.EndTime   = TimeSpan.FromSeconds(90);
        _jobRepo.Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>())).ReturnsAsync([job]);

        await _service.ProcessQueueAsync();

        _media.Verify(m => m.TrimAsync(
            "/clips/Replay.mp4", "/out/clip.mp4",
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(90),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessQueueAsync_HighlightRange_WinsOverExplicitRange()
    {
        var job = PendingJob(1, "/out/clip.mp4");
        job.StartTime = TimeSpan.FromSeconds(30);
        job.EndTime   = TimeSpan.FromSeconds(90);
        job.Highlight = new Highlight
        {
            Id = 5,
            ClipId = 1,
            StartTime = TimeSpan.FromSeconds(10),
            EndTime   = TimeSpan.FromSeconds(20)
        };
        _jobRepo.Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>())).ReturnsAsync([job]);

        await _service.ProcessQueueAsync();

        _media.Verify(m => m.TrimAsync(
            "/clips/Replay.mp4", "/out/clip.mp4",
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessQueueAsync_DestructiveExport_TrashesTheOriginal()
    {
        var job = PendingJob(1, "/out/clip.mp4");
        job.TrimMode = TrimMode.Destructive;
        job.DeleteOriginalAfterExport = true;
        _jobRepo.Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>())).ReturnsAsync([job]);

        await _service.ProcessQueueAsync();

        _clipService.Verify(s => s.TrashAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(ExportJobStatus.Completed, job.Status);
    }

    [Fact]
    public async Task ProcessQueueAsync_NonDestructiveExport_LeavesTheOriginalAlone()
    {
        _jobRepo
            .Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([PendingJob(1, "/out/clip.mp4")]);

        await _service.ProcessQueueAsync();

        _clipService.Verify(
            s => s.TrashAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessQueueAsync_Cancellation_MarksTheJobCancelledAndRethrows()
    {
        var job = PendingJob(1, "/out/clip.mp4");
        _jobRepo.Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>())).ReturnsAsync([job]);

        _media
            .Setup(m => m.TrimAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.ProcessQueueAsync());

        Assert.Equal(ExportJobStatus.Cancelled, job.Status);
    }

    [Fact]
    public async Task ProcessQueueAsync_EmptyQueue_DoesNothing()
    {
        _jobRepo.Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await _service.ProcessQueueAsync();

        _media.Verify(m => m.TrimAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- CancelJobAsync ----

    [Fact]
    public async Task CancelJobAsync_PendingJob_IsCancelled()
    {
        var job = PendingJob(1, "/out/clip.mp4");
        _jobRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        await _service.CancelJobAsync(1);

        Assert.Equal(ExportJobStatus.Cancelled, job.Status);
        Assert.NotNull(job.CompletedAt);
    }

    [Fact]
    public async Task CancelJobAsync_AlreadyCompletedJob_IsLeftAlone()
    {
        var job = PendingJob(1, "/out/clip.mp4");
        job.Status = ExportJobStatus.Completed;
        _jobRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        await _service.CancelJobAsync(1);

        Assert.Equal(ExportJobStatus.Completed, job.Status);
        _jobRepo.Verify(
            r => r.UpdateAsync(It.IsAny<ExportJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancelJobAsync_UnknownJob_IsANoOp()
    {
        _jobRepo
            .Setup(r => r.GetByIdAsync(404, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExportJob?)null);

        await _service.CancelJobAsync(404);

        _jobRepo.Verify(
            r => r.UpdateAsync(It.IsAny<ExportJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
