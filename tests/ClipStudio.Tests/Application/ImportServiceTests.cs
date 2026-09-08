using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Application.Services;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;
using ClipStudio.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClipStudio.Tests.Application;

/// <summary>
/// Unit tests for <see cref="ImportService"/> covering metadata capture, filename parsing,
/// game alias auto-apply, duplicate handling and folder scanning. All file-system access goes
/// through <see cref="FakeFileSystem"/> so no test touches disk.
/// </summary>
public sealed class ImportServiceTests
{
    private const string SourcePath = "/clips";
    private const string DataRoot = "/appdata/ClipStudio";

    private readonly Mock<IClipRepository> _clipRepo = new();
    private readonly Mock<ISourceFolderRepository> _folderRepo = new();
    private readonly Mock<IMediaService> _media = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly Mock<IPlayerRepository> _playerRepo = new();
    private readonly Mock<IGameTagAliasService> _aliases = new();
    private readonly Mock<ITranscriptionService> _transcription = new();
    private readonly FakeFileSystem _fileSystem = new();
    private readonly AppSettings _appSettings = new();
    private readonly ImportService _service;

    /// <summary>Sets up the service with permissive defaults; each test overrides what it cares about.</summary>
    public ImportServiceTests()
    {
        _appSettings.AutoApplyMePlayerOnImport = false;
        _appSettings.TranscriptionEnabled = false;
        _settings.Setup(s => s.Current).Returns(_appSettings);

        _media
            .Setup(m => m.GetMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaMetadata
            {
                Duration = TimeSpan.FromMinutes(3),
                Width = 1920,
                Height = 1080,
                Codec = "h264",
                FileSizeBytes = 12345,
                AudioStreamCount = 2
            });

        _media
            .Setup(m => m.GenerateThumbnailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/appdata/ClipStudio/media-cache/thumb.png");

        _media
            .Setup(m => m.GeneratePreviewStripAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/appdata/ClipStudio/media-cache/strip.jpg");

        _clipRepo
            .Setup(r => r.ExistsByFilePathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _aliases
            .Setup(a => a.FindByAliasAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameTagAlias?)null);

        _service = new ImportService(
            _clipRepo.Object,
            _folderRepo.Object,
            _media.Object,
            _settings.Object,
            _playerRepo.Object,
            _aliases.Object,
            _transcription.Object,
            _fileSystem,
            new AppDataPaths(DataRoot),
            NullLogger<ImportService>.Instance);
    }

    /// <summary>Captures the clip handed to <see cref="IClipRepository.AddAsync"/> and assigns it an id.</summary>
    /// <param name="id">The identifier to stamp onto the persisted clip.</param>
    /// <returns>A holder whose <c>Value</c> is set once the clip is added.</returns>
    private StrongBox<Clip?> CaptureAddedClip(int id = 1)
    {
        var box = new StrongBox<Clip?>(null);
        _clipRepo
            .Setup(r => r.AddAsync(It.IsAny<Clip>(), It.IsAny<CancellationToken>()))
            .Callback<Clip, CancellationToken>((c, _) => { c.Id = id; box.Value = c; })
            .Returns(Task.CompletedTask);
        return box;
    }

    /// <summary>Minimal mutable holder, so callbacks can publish a value to the test body.</summary>
    /// <typeparam name="T">The held type.</typeparam>
    /// <param name="Value">The held value.</param>
    private sealed record StrongBox<T>(T Value)
    {
        /// <summary>Gets or sets the held value.</summary>
        public T Value { get; set; } = Value;
    }

    // ---- ImportFileAsync ----

    [Fact]
    public async Task ImportFileAsync_UnsupportedExtension_Fails()
    {
        var result = await _service.ImportFileAsync($"{SourcePath}/notes.txt", 1);

        Assert.False(result.Success);
        Assert.Contains("Unsupported file type", result.Message);
        _clipRepo.Verify(r => r.AddAsync(It.IsAny<Clip>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportFileAsync_MissingFile_Fails()
    {
        var result = await _service.ImportFileAsync($"{SourcePath}/ghost.mp4", 1);

        Assert.False(result.Success);
        Assert.Contains("File not found", result.Message);
    }

    [Fact]
    public async Task ImportFileAsync_AlreadyInLibrary_Skips()
    {
        var path = $"{SourcePath}/existing.mp4";
        _fileSystem.AddFile(path);
        _clipRepo
            .Setup(r => r.ExistsByFilePathAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.ImportFileAsync(path, 1);

        Assert.True(result.Success);
        Assert.Null(result.Clip);
        Assert.Contains("Already in library", result.Message);
        _clipRepo.Verify(r => r.AddAsync(It.IsAny<Clip>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportFileAsync_CapturesMetadataAndArtefacts()
    {
        var path = $"{SourcePath}/Replay.mp4";
        _fileSystem.AddFile(path);
        var added = CaptureAddedClip();

        var result = await _service.ImportFileAsync(path, 7);

        Assert.True(result.Success);
        var clip = added.Value;
        Assert.NotNull(clip);
        Assert.Equal(7, clip!.SourceFolderId);
        Assert.Equal(path, clip.FilePath);
        Assert.Equal("Replay.mp4", clip.FileName);
        Assert.Equal(TimeSpan.FromMinutes(3), clip.Duration);
        Assert.Equal("1920x1080", clip.Resolution);
        Assert.Equal(12345, clip.FileSizeBytes);
        Assert.Equal("/appdata/ClipStudio/media-cache/thumb.png", clip.ThumbnailPath);
        Assert.Equal("/appdata/ClipStudio/media-cache/strip.jpg", clip.PreviewStripPath);
    }

    [Fact]
    public async Task ImportFileAsync_NewClip_LandsInUnreviewedQueue()
    {
        var path = $"{SourcePath}/Replay.mp4";
        _fileSystem.AddFile(path);
        var added = CaptureAddedClip();

        await _service.ImportFileAsync(path, 1);

        Assert.Equal(ClipStatus.Unreviewed, added.Value!.Status);
    }

    [Fact]
    public async Task ImportFileAsync_BracketToken_BecomesSuggestedGameName()
    {
        var path = $"{SourcePath}/Replay 2025-03-03 22-49-45 [Deep Rock Galactic].mp4";
        _fileSystem.AddFile(path);
        var added = CaptureAddedClip();

        await _service.ImportFileAsync(path, 1);

        Assert.Equal("Deep Rock Galactic", added.Value!.SuggestedGameName);
    }

    [Fact]
    public async Task ImportFileAsync_FilenameTimestamp_WinsOverFileCreationTime()
    {
        var path = $"{SourcePath}/Replay 2025-03-03 22-49-45.mp4";
        _fileSystem.AddFile(path, creationTimeUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var added = CaptureAddedClip();

        await _service.ImportFileAsync(path, 1);

        Assert.Equal(new DateTime(2025, 3, 3, 22, 49, 45), added.Value!.CreatedAt);
    }

    [Fact]
    public async Task ImportFileAsync_NoFilenameTimestamp_FallsBackToEmbeddedCreationTime()
    {
        var embedded = new DateTime(2025, 5, 5, 12, 0, 0, DateTimeKind.Utc);
        _media
            .Setup(m => m.GetMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaMetadata
            {
                Duration = TimeSpan.FromMinutes(1),
                EmbeddedCreationTime = embedded
            });

        var path = $"{SourcePath}/Replay.mp4";
        _fileSystem.AddFile(path, creationTimeUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var added = CaptureAddedClip();

        await _service.ImportFileAsync(path, 1);

        Assert.Equal(embedded, added.Value!.CreatedAt);
    }

    [Fact]
    public async Task ImportFileAsync_NoTimestampAnywhere_FallsBackToFileCreationTime()
    {
        var created = new DateTime(2026, 1, 1, 9, 30, 0, DateTimeKind.Utc);
        var path = $"{SourcePath}/Replay.mp4";
        _fileSystem.AddFile(path, creationTimeUtc: created);
        var added = CaptureAddedClip();

        await _service.ImportFileAsync(path, 1);

        Assert.Equal(created, added.Value!.CreatedAt);
    }

    [Fact]
    public async Task ImportFileAsync_KnownGameAlias_AppliesTagAndClearsSuggestion()
    {
        var path = $"{SourcePath}/Replay [Deep Rock Galactic].mp4";
        _fileSystem.AddFile(path);
        var added = CaptureAddedClip(id: 42);

        _aliases
            .Setup(a => a.FindByAliasAsync("Deep Rock Galactic", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameTagAlias { Id = 1, TagId = 99, AliasString = "Deep Rock Galactic" });

        await _service.ImportFileAsync(path, 1);

        _clipRepo.Verify(r => r.AddClipTagAsync(42, 99, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(added.Value!.SuggestedGameName);
    }

    [Fact]
    public async Task ImportFileAsync_UnknownGameAlias_KeepsSuggestionForTheUnreviewedQueue()
    {
        var path = $"{SourcePath}/Replay [Some Game].mp4";
        _fileSystem.AddFile(path);
        var added = CaptureAddedClip();

        await _service.ImportFileAsync(path, 1);

        Assert.Equal("Some Game", added.Value!.SuggestedGameName);
        _clipRepo.Verify(
            r => r.AddClipTagAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ImportFileAsync_AutoApplyMePlayer_TagsEveryMePlayer_WhenEnabled()
    {
        _appSettings.AutoApplyMePlayerOnImport = true;
        _playerRepo
            .Setup(r => r.GetMePlayersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Player { Id = 3, DisplayName = "Me", IsMe = true }]);

        var path = $"{SourcePath}/Replay.mp4";
        _fileSystem.AddFile(path);
        CaptureAddedClip(id: 11);

        await _service.ImportFileAsync(path, 1);

        _playerRepo.Verify(r => r.TagClipAsync(11, 3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportFileAsync_AutoApplyMePlayerDisabled_DoesNotTag()
    {
        var path = $"{SourcePath}/Replay.mp4";
        _fileSystem.AddFile(path);
        CaptureAddedClip();

        await _service.ImportFileAsync(path, 1);

        _playerRepo.Verify(r => r.GetMePlayersAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportFileAsync_MediaFailure_IsReportedNotThrown()
    {
        _media
            .Setup(m => m.GetMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ffprobe exploded"));

        var path = $"{SourcePath}/Replay.mp4";
        _fileSystem.AddFile(path);

        var result = await _service.ImportFileAsync(path, 1);

        Assert.False(result.Success);
        Assert.Contains("ffprobe exploded", result.Message);
    }

    [Fact]
    public async Task ImportFileAsync_CreatesMediaCacheDirectory()
    {
        var path = $"{SourcePath}/Replay.mp4";
        _fileSystem.AddFile(path);
        CaptureAddedClip();

        await _service.ImportFileAsync(path, 1);

        Assert.True(_fileSystem.DirectoryExists($"{DataRoot}/media-cache"));
    }

    // ---- ScanFolderAsync ----

    /// <summary>Registers a source folder with the given id and path.</summary>
    /// <param name="id">The source folder identifier.</param>
    /// <param name="path">The folder path.</param>
    /// <returns>The registered folder.</returns>
    private SourceFolder SetUpFolder(int id = 1, string path = SourcePath)
    {
        var folder = new SourceFolder { Id = id, Path = path, IsActive = true };
        _folderRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folder);
        return folder;
    }

    [Fact]
    public async Task ScanFolderAsync_UnknownFolder_Throws()
    {
        _folderRepo
            .Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SourceFolder?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ScanFolderAsync(99));
    }

    [Fact]
    public async Task ScanFolderAsync_MissingDirectory_ReturnsEmpty()
    {
        SetUpFolder(path: "/gone");

        var results = await _service.ScanFolderAsync(1);

        Assert.Empty(results);
    }

    [Fact]
    public async Task ScanFolderAsync_ImportsOnlySupportedVideoFiles()
    {
        SetUpFolder();
        _fileSystem
            .AddFile($"{SourcePath}/a.mp4")
            .AddFile($"{SourcePath}/b.mkv")
            .AddFile($"{SourcePath}/notes.txt")
            .AddFile("/clips/nested/c.mp4");

        CaptureAddedClip();

        var results = await _service.ScanFolderAsync(1);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
    }

    [Fact]
    public async Task ScanFolderAsync_ReportsProgressPerFile()
    {
        SetUpFolder();
        _fileSystem.AddFile($"{SourcePath}/a.mp4").AddFile($"{SourcePath}/b.mp4");
        CaptureAddedClip();

        var reports = new List<ImportProgressReport>();
        await _service.ScanFolderAsync(1, new Progress<ImportProgressReport>(reports.Add));

        // Progress<T> posts asynchronously; drain the callbacks before asserting.
        await Task.Delay(50);

        Assert.All(reports, r => Assert.Equal(2, r.TotalFiles));
        Assert.Equal(2, reports.Count(r => r.CurrentFileIndex is 1 or 2));
    }

    [Fact]
    public async Task ScanFolderAsync_StampsLastScannedAt()
    {
        var folder = SetUpFolder();
        _fileSystem.AddDirectory(SourcePath);

        await _service.ScanFolderAsync(1);

        Assert.NotNull(folder.LastScannedAt);
        _folderRepo.Verify(r => r.UpdateAsync(folder, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ScanFolderAsync_Cancellation_Throws()
    {
        SetUpFolder();
        _fileSystem.AddFile($"{SourcePath}/a.mp4");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.ScanFolderAsync(1, null, cts.Token));
    }
}
