using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Application.Services;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;
using ClipStudio.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClipStudio.Tests.Application;

/// <summary>
/// Unit tests for <see cref="LibrarySanitizerService"/> covering artefact regeneration, broken
/// clip flagging, ghost repair, orphan cache cleanup and the reverse scan of the app trash
/// subfolders. All file-system access is served by <see cref="FakeFileSystem"/>.
/// </summary>
public sealed class LibrarySanitizerServiceTests
{
    private const string DataRoot = "/appdata/ClipStudio";
    private const string MediaCache = "/appdata/ClipStudio/media-cache";
    private const string AudioCache = "/appdata/ClipStudio/audio_cache";
    private const string SourcePath = "/clips";
    private const string TrashDir = "/clips/.clipstudio_trash";

    private readonly Mock<IClipRepository> _clipRepo = new();
    private readonly Mock<IHighlightRepository> _highlightRepo = new();
    private readonly Mock<IMediaService> _media = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly Mock<ITranscriptionRepository> _transcriptionRepo = new();
    private readonly Mock<ISourceFolderRepository> _folderRepo = new();
    private readonly Mock<IRecycleBinService> _recycleBin = new();
    private readonly FakeFileSystem _fileSystem = new();
    private readonly AppSettings _appSettings = new();
    private readonly LibrarySanitizerService _service;

    /// <summary>Sets up an empty library with one source folder and successful media generation.</summary>
    public LibrarySanitizerServiceTests()
    {
        _settings.Setup(s => s.Current).Returns(_appSettings);

        _clipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _clipRepo.Setup(r => r.GetTrashedAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _transcriptionRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _folderRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SourceFolder { Id = 1, Path = SourcePath, IsActive = true }]);

        _media
            .Setup(m => m.GenerateThumbnailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync($"{MediaCache}/regenerated-thumb.png");

        _media
            .Setup(m => m.GeneratePreviewStripAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync($"{MediaCache}/regenerated-strip.jpg");

        _recycleBin.Setup(r => r.TryMoveToRecycleBin(It.IsAny<string>())).Returns(true);

        _service = new LibrarySanitizerService(
            _clipRepo.Object,
            _highlightRepo.Object,
            _media.Object,
            _settings.Object,
            _transcriptionRepo.Object,
            _folderRepo.Object,
            _recycleBin.Object,
            _fileSystem,
            new AppDataPaths(DataRoot),
            NullLogger<LibrarySanitizerService>.Instance);
    }

    /// <summary>Builds a clip whose source file and cached artefacts all exist on the fake disk.</summary>
    /// <param name="id">The clip identifier.</param>
    /// <param name="fileName">The clip's file name inside the source folder.</param>
    /// <returns>A healthy clip.</returns>
    private Clip HealthyClip(int id = 1, string fileName = "Replay.mp4")
    {
        var clip = new Clip
        {
            Id = id,
            SourceFolderId = 1,
            FilePath = $"{SourcePath}/{fileName}",
            FileName = fileName,
            Duration = TimeSpan.FromMinutes(2),
            CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ThumbnailPath = $"{MediaCache}/clip{id}.png",
            PreviewStripPath = $"{MediaCache}/clip{id}_strip.jpg",
            Highlights = []
        };

        _fileSystem
            .AddFile(clip.FilePath, creationTimeUtc: clip.CreatedAt)
            .AddFile(clip.ThumbnailPath)
            .AddFile(clip.PreviewStripPath);

        return clip;
    }

    /// <summary>Registers the given clips as the active library.</summary>
    /// <param name="clips">The clips the repository should return.</param>
    private void SetUpLibrary(params Clip[] clips)
        => _clipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(clips);

    // ---- Artefact regeneration ----

    [Fact]
    public async Task SanitizeAsync_MissingThumbnail_IsRegenerated()
    {
        var clip = HealthyClip();
        _fileSystem.DeleteFile(clip.ThumbnailPath!);
        SetUpLibrary(clip);

        await _service.SanitizeAsync();

        Assert.Equal($"{MediaCache}/regenerated-thumb.png", clip.ThumbnailPath);
        _clipRepo.Verify(r => r.UpdateAsync(clip, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SanitizeAsync_MissingPreviewStrip_IsRegenerated()
    {
        var clip = HealthyClip();
        _fileSystem.DeleteFile(clip.PreviewStripPath!);
        SetUpLibrary(clip);

        await _service.SanitizeAsync();

        Assert.Equal($"{MediaCache}/regenerated-strip.jpg", clip.PreviewStripPath);
    }

    [Fact]
    public async Task SanitizeAsync_IntactArtefacts_AreLeftAlone()
    {
        SetUpLibrary(HealthyClip());

        await _service.SanitizeAsync();

        _media.Verify(m => m.GenerateThumbnailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _media.Verify(m => m.GeneratePreviewStripAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SanitizeAsync_MissingHighlightThumbnail_IsRegeneratedAtMidpoint()
    {
        var clip = HealthyClip();
        clip.Highlights =
        [
            new Highlight
            {
                Id = 4,
                ClipId = clip.Id,
                StartTime = TimeSpan.FromSeconds(10),
                EndTime = TimeSpan.FromSeconds(30)
            }
        ];
        SetUpLibrary(clip);

        await _service.SanitizeAsync();

        _media.Verify(m => m.GenerateThumbnailAsync(
            clip.FilePath, It.IsAny<string>(), TimeSpan.FromSeconds(20), "hl4",
            It.IsAny<CancellationToken>()), Times.Once);
        _highlightRepo.Verify(
            r => r.UpdateAsync(clip.Highlights.First(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SanitizeAsync_MediaFailure_DoesNotAbortTheSweep()
    {
        var clip = HealthyClip();
        _fileSystem.DeleteFile(clip.ThumbnailPath!);
        SetUpLibrary(clip);

        _media
            .Setup(m => m.GenerateThumbnailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ffmpeg exploded"));

        await _service.SanitizeAsync();

        Assert.Equal($"{MediaCache}/clip1.png", clip.ThumbnailPath);
    }

    // ---- Broken clip handling ----

    [Fact]
    public async Task SanitizeAsync_MissingSourceFile_MarksTheClipBroken()
    {
        var clip = HealthyClip();
        _fileSystem.DeleteFile(clip.FilePath);
        SetUpLibrary(clip);

        await _service.SanitizeAsync();

        Assert.True(clip.IsBroken);
        _clipRepo.Verify(r => r.UpdateAsync(clip, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SanitizeAsync_RediscoveredFile_ClearsIsBroken()
    {
        var clip = HealthyClip();
        clip.IsBroken = true;
        SetUpLibrary(clip);

        await _service.SanitizeAsync();

        Assert.False(clip.IsBroken);
        _clipRepo.Verify(r => r.UpdateAsync(clip, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SanitizeAsync_AlreadyBrokenClip_IsLeftForTheUserToResolve()
    {
        var clip = HealthyClip();
        clip.IsBroken = true;
        _fileSystem.DeleteFile(clip.FilePath);
        SetUpLibrary(clip);

        await _service.SanitizeAsync();

        Assert.True(clip.IsBroken);
        Assert.False(clip.IsDeleted);
        _clipRepo.Verify(
            r => r.UpdateAsync(It.IsAny<Clip>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SanitizeAsync_GhostClipWithLiveTrashPath_IsMarkedDeleted()
    {
        var clip = HealthyClip();
        _fileSystem.DeleteFile(clip.FilePath);
        clip.TrashPath = $"{TrashDir}/Replay.mp4";
        _fileSystem.AddFile(clip.TrashPath);
        SetUpLibrary(clip);

        await _service.SanitizeAsync();

        Assert.True(clip.IsDeleted);
        Assert.NotNull(clip.DeletedAt);
        Assert.False(clip.IsBroken);
    }

    [Fact]
    public async Task SanitizeAsync_GhostClipWithoutTrashPath_IsRecoveredFromTheTrashSubfolder()
    {
        var clip = HealthyClip();
        _fileSystem.DeleteFile(clip.FilePath);
        _fileSystem.AddFile($"{TrashDir}/Replay_20250101_120000.mp4");
        SetUpLibrary(clip);

        await _service.SanitizeAsync();

        Assert.True(clip.IsDeleted);
        Assert.Equal($"{TrashDir}/Replay_20250101_120000.mp4", clip.TrashPath);
    }

    // ---- Orphan cache cleanup ----

    [Fact]
    public async Task SanitizeAsync_UnreferencedCacheFile_IsDeleted()
    {
        var clip = HealthyClip();
        _fileSystem.AddFile($"{MediaCache}/orphan.png");
        SetUpLibrary(clip);

        await _service.SanitizeAsync();

        Assert.False(_fileSystem.FileExists($"{MediaCache}/orphan.png"));
        Assert.True(_fileSystem.FileExists(clip.ThumbnailPath!));
        Assert.True(_fileSystem.FileExists(clip.PreviewStripPath!));
    }

    [Fact]
    public async Task SanitizeAsync_TrashedClipArtefacts_SurviveOrphanCleanup()
    {
        var trashed = new Clip
        {
            Id = 9,
            FilePath = $"{SourcePath}/Old.mp4",
            FileName = "Old.mp4",
            IsDeleted = true,
            ThumbnailPath = $"{MediaCache}/clip9.png",
            PreviewStripPath = $"{MediaCache}/clip9_strip.jpg",
            Highlights = []
        };
        _fileSystem.AddFile(trashed.ThumbnailPath).AddFile(trashed.PreviewStripPath);
        _clipRepo.Setup(r => r.GetTrashedAsync(It.IsAny<CancellationToken>())).ReturnsAsync([trashed]);

        await _service.SanitizeAsync();

        Assert.True(_fileSystem.FileExists(trashed.ThumbnailPath));
        Assert.True(_fileSystem.FileExists(trashed.PreviewStripPath));
    }

    [Fact]
    public async Task SanitizeAsync_AudioCache_KeepsOnlyFilesForLiveClips()
    {
        var clip = HealthyClip(id: 3);
        _fileSystem
            .AddFile($"{AudioCache}/clip_3_audio_preview.mkv")
            .AddFile($"{AudioCache}/clip_99_audio_preview.mkv")
            .AddFile($"{AudioCache}/stray.mkv");
        SetUpLibrary(clip);

        await _service.SanitizeAsync();

        Assert.True(_fileSystem.FileExists($"{AudioCache}/clip_3_audio_preview.mkv"));
        Assert.False(_fileSystem.FileExists($"{AudioCache}/clip_99_audio_preview.mkv"));
        Assert.False(_fileSystem.FileExists($"{AudioCache}/stray.mkv"));
    }

    // ---- SRT cleanup ----

    [Fact]
    public async Task SanitizeAsync_TranscriptionForDeletedClip_IsRemovedWithItsSrtFile()
    {
        var srtPath = "/srt/gone.srt";
        _fileSystem.AddFile(srtPath);
        _transcriptionRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Transcription { Id = 2, ClipId = 404, SrtFilePath = srtPath }]);

        await _service.SanitizeAsync();

        Assert.False(_fileSystem.FileExists(srtPath));
        _transcriptionRepo.Verify(r => r.DeleteAsync(2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SanitizeAsync_TranscriptionForLiveClip_IsKept()
    {
        var clip = HealthyClip();
        var srtPath = "/srt/live.srt";
        _fileSystem.AddFile(srtPath);
        SetUpLibrary(clip);
        _transcriptionRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Transcription { Id = 2, ClipId = clip.Id, SrtFilePath = srtPath }]);

        await _service.SanitizeAsync();

        Assert.True(_fileSystem.FileExists(srtPath));
        _transcriptionRepo.Verify(
            r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SanitizeAsync_UntrackedSrtInTheOutputFolder_IsDeleted()
    {
        _appSettings.TranscriptionSrtFolder = "/srt";
        _fileSystem.AddFile("/srt/untracked.srt").AddFile("/srt/notes.txt");

        await _service.SanitizeAsync();

        Assert.False(_fileSystem.FileExists("/srt/untracked.srt"));
        Assert.True(_fileSystem.FileExists("/srt/notes.txt"));
    }

    // ---- Trash reverse scan ----

    [Fact]
    public async Task SanitizeAsync_TrashFileMatchingALiveClip_SoftDeletesThatClip()
    {
        var clip = HealthyClip();
        _fileSystem.AddFile($"{TrashDir}/Replay.mp4");
        SetUpLibrary(clip);

        await _service.SanitizeAsync();

        Assert.True(clip.IsDeleted);
        Assert.False(clip.IsBroken);
        Assert.Equal($"{TrashDir}/Replay.mp4", clip.TrashPath);
        _recycleBin.Verify(r => r.TryMoveToRecycleBin(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SanitizeAsync_UnreferencedTrashFile_IsSentToTheRecycleBin()
    {
        _fileSystem.AddFile($"{TrashDir}/intruder.mp4");

        await _service.SanitizeAsync();

        _recycleBin.Verify(
            r => r.TryMoveToRecycleBin($"{TrashDir}/intruder.mp4"), Times.Once);
    }

    [Fact]
    public async Task SanitizeAsync_TrashFileOfAnAlreadyTrashedClip_IsLeftAlone()
    {
        var trashPath = $"{TrashDir}/Old.mp4";
        _fileSystem.AddFile(trashPath);
        _clipRepo
            .Setup(r => r.GetTrashedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Clip
            {
                Id = 9,
                FilePath = $"{SourcePath}/Old.mp4",
                FileName = "Old.mp4",
                IsDeleted = true,
                TrashPath = trashPath,
                Highlights = []
            }]);

        await _service.SanitizeAsync();

        _recycleBin.Verify(r => r.TryMoveToRecycleBin(It.IsAny<string>()), Times.Never);
    }

    // ---- Progress and cancellation ----

    [Fact]
    public async Task SanitizeAsync_ReportsASummary()
    {
        var reports = new List<string>();

        await _service.SanitizeAsync(new Progress<string>(reports.Add));

        // Progress<T> posts asynchronously; drain the callbacks before asserting.
        await Task.Delay(50);

        Assert.Contains(reports, r => r.StartsWith("Sanitize complete:"));
    }

    [Fact]
    public async Task SanitizeAsync_Cancellation_Throws()
    {
        SetUpLibrary(HealthyClip());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.SanitizeAsync(null, cts.Token));
    }

    [Fact]
    public async Task SanitizeAsync_CreatesTheCacheDirectories()
    {
        await _service.SanitizeAsync();

        Assert.True(_fileSystem.DirectoryExists(MediaCache));
        Assert.True(_fileSystem.DirectoryExists(AudioCache));
    }

    // ---- Out-of-range highlight ranges ----

    /// <summary>Adds a highlight with an existing thumbnail so only the range check can fire.</summary>
    /// <param name="clip">The clip to attach the highlight to.</param>
    /// <param name="start">The highlight start.</param>
    /// <param name="end">The highlight end.</param>
    /// <returns>The attached highlight.</returns>
    private Highlight AddHighlight(Clip clip, TimeSpan start, TimeSpan end)
    {
        var highlight = new Highlight
        {
            Id            = clip.Id * 100,
            ClipId        = clip.Id,
            Label         = "highlight",
            StartTime     = start,
            EndTime       = end,
            ThumbnailPath = $"{MediaCache}/hl{clip.Id}.png",
        };

        _fileSystem.AddFile(highlight.ThumbnailPath);
        clip.Highlights.Add(highlight);
        return highlight;
    }

    [Fact]
    public async Task SanitizeAsync_HighlightEndPastTheClip_IsPulledBack()
    {
        // The demo library's "boss fight": 120-210s on a clip of about 179s.
        var clip = HealthyClip();
        var highlight = AddHighlight(clip, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(210));
        SetUpLibrary(clip);

        await _service.SanitizeAsync();

        Assert.Equal(TimeSpan.FromSeconds(60), highlight.StartTime);
        Assert.Equal(clip.Duration, highlight.EndTime);
        _highlightRepo.Verify(r => r.UpdateAsync(highlight, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SanitizeAsync_HighlightEntirelyPastTheClip_IsAnchoredToTheEnd()
    {
        // The demo library's "lucky escape": starts after the clip has already finished.
        var clip = HealthyClip();
        var highlight = AddHighlight(clip, TimeSpan.FromSeconds(180), TimeSpan.FromSeconds(210));
        SetUpLibrary(clip);

        await _service.SanitizeAsync();

        Assert.Equal(clip.Duration, highlight.EndTime);
        Assert.True(highlight.StartTime < highlight.EndTime);
        Assert.True(highlight.EndTime <= clip.Duration);
        // The label is the user's work and survives a bad time range.
        Assert.Equal("highlight", highlight.Label);
    }

    [Fact]
    public async Task SanitizeAsync_HighlightInsideTheClip_IsLeftAlone()
    {
        var clip = HealthyClip();
        var highlight = AddHighlight(clip, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60));
        SetUpLibrary(clip);

        await _service.SanitizeAsync();

        Assert.Equal(TimeSpan.FromSeconds(30), highlight.StartTime);
        Assert.Equal(TimeSpan.FromSeconds(60), highlight.EndTime);
        _highlightRepo.Verify(
            r => r.UpdateAsync(It.IsAny<Highlight>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SanitizeAsync_ClipWithUnknownDuration_LeavesHighlightsAlone()
    {
        // Duration zero means it was never probed; clamping to it would destroy every range.
        var clip = HealthyClip();
        clip.Duration = TimeSpan.Zero;
        var highlight = AddHighlight(clip, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60));
        SetUpLibrary(clip);

        await _service.SanitizeAsync();

        Assert.Equal(TimeSpan.FromSeconds(60), highlight.EndTime);
        _highlightRepo.Verify(
            r => r.UpdateAsync(It.IsAny<Highlight>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
