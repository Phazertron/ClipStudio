using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Services;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;
using ClipStudio.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClipStudio.Tests.Application;

/// <summary>
/// Unit tests for <see cref="HighlightService"/> covering validation, overlap tolerance,
/// tagging, rating and the midpoint thumbnail lifecycle.
/// </summary>
public sealed class HighlightServiceTests
{
    private const string ClipPath = "/clips/Replay.mp4";
    private const string ThumbnailPath = "/cache/clip1.png";

    private readonly Mock<IHighlightRepository> _highlightRepo = new();
    private readonly Mock<IClipRepository> _clipRepo = new();
    private readonly Mock<IMediaService> _media = new();
    private readonly FakeFileSystem _fileSystem = new();
    private readonly HighlightService _service;

    private int _nextHighlightId = 1;

    /// <summary>Sets up a service whose repository assigns ids and whose clip file exists.</summary>
    public HighlightServiceTests()
    {
        _fileSystem.AddFile(ClipPath);

        _highlightRepo
            .Setup(r => r.AddAsync(It.IsAny<Highlight>(), It.IsAny<CancellationToken>()))
            .Callback<Highlight, CancellationToken>((h, _) => h.Id = _nextHighlightId++)
            .Returns(Task.CompletedTask);

        _clipRepo
            .Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clip
            {
                Id = 1,
                FilePath = ClipPath,
                FileName = "Replay.mp4",
                ThumbnailPath = ThumbnailPath
            });

        _media
            .Setup(m => m.GenerateThumbnailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/cache/clip1_hl.png");

        _service = new HighlightService(
            _highlightRepo.Object,
            _clipRepo.Object,
            _media.Object,
            _fileSystem,
            NullLogger<HighlightService>.Instance);
    }

    /// <summary>Registers an existing highlight so Update/Tag/Rating paths can find it.</summary>
    /// <param name="id">The highlight identifier.</param>
    /// <param name="start">The start of the highlight range.</param>
    /// <param name="end">The end of the highlight range.</param>
    /// <returns>The registered highlight.</returns>
    private Highlight SetUpExisting(int id, TimeSpan start, TimeSpan end)
    {
        var highlight = new Highlight
        {
            Id = id,
            ClipId = 1,
            StartTime = start,
            EndTime = end,
            HighlightTags = []
        };

        _highlightRepo
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(highlight);

        return highlight;
    }

    // ---- CreateAsync ----

    [Fact]
    public async Task CreateAsync_EndBeforeStart_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(
            1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task CreateAsync_ZeroLengthRange_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(
            1, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task CreateAsync_PersistsRangeAndLabel()
    {
        var highlight = await _service.CreateAsync(
            1, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), "Ace", "clutch");

        Assert.Equal(1, highlight.ClipId);
        Assert.Equal(TimeSpan.FromSeconds(10), highlight.StartTime);
        Assert.Equal(TimeSpan.FromSeconds(30), highlight.EndTime);
        Assert.Equal("Ace", highlight.Label);
        Assert.Equal("clutch", highlight.Notes);
        _highlightRepo.Verify(
            r => r.AddAsync(It.IsAny<Highlight>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_OverlappingRanges_AreAllowed()
    {
        var first  = await _service.CreateAsync(1, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(30));
        var second = await _service.CreateAsync(1, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(50));

        Assert.NotEqual(first.Id, second.Id);
        _highlightRepo.Verify(
            r => r.AddAsync(It.IsAny<Highlight>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CreateAsync_GeneratesThumbnailAtMidpoint()
    {
        await _service.CreateAsync(1, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));

        _media.Verify(m => m.GenerateThumbnailAsync(
            ClipPath,
            It.IsAny<string>(),
            TimeSpan.FromSeconds(20),
            "hl1",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_MissingClipFile_SkipsThumbnailButStillCreates()
    {
        _fileSystem.DeleteFile(ClipPath);

        var highlight = await _service.CreateAsync(1, TimeSpan.Zero, TimeSpan.FromSeconds(10));

        Assert.Null(highlight.ThumbnailPath);
        _media.Verify(m => m.GenerateThumbnailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ThumbnailFailure_DoesNotFailTheHighlight()
    {
        _media
            .Setup(m => m.GenerateThumbnailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ffmpeg exploded"));

        var highlight = await _service.CreateAsync(1, TimeSpan.Zero, TimeSpan.FromSeconds(10));

        Assert.Equal(1, highlight.Id);
        Assert.Null(highlight.ThumbnailPath);
    }

    // ---- UpdateAsync ----

    [Fact]
    public async Task UpdateAsync_EndBeforeStart_Throws()
    {
        SetUpExisting(1, TimeSpan.Zero, TimeSpan.FromSeconds(10));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(
            1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), null, null));
    }

    [Fact]
    public async Task UpdateAsync_UnknownHighlight_Throws()
    {
        _highlightRepo
            .Setup(r => r.GetByIdAsync(404, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Highlight?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateAsync(
            404, TimeSpan.Zero, TimeSpan.FromSeconds(5), null, null));
    }

    [Fact]
    public async Task UpdateAsync_TimeRangeChanged_RegeneratesThumbnailAtNewMidpoint()
    {
        var highlight = SetUpExisting(1, TimeSpan.Zero, TimeSpan.FromSeconds(10));

        await _service.UpdateAsync(
            1, TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(60), "Renamed", null);

        _media.Verify(m => m.GenerateThumbnailAsync(
            ClipPath,
            It.IsAny<string>(),
            TimeSpan.FromSeconds(50),
            "hl1",
            It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal("/cache/clip1_hl.png", highlight.ThumbnailPath);
    }

    [Fact]
    public async Task UpdateAsync_LabelOnlyChange_LeavesThumbnailAlone()
    {
        SetUpExisting(1, TimeSpan.Zero, TimeSpan.FromSeconds(10));

        await _service.UpdateAsync(1, TimeSpan.Zero, TimeSpan.FromSeconds(10), "Renamed", null);

        _media.Verify(m => m.GenerateThumbnailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_MissingClipFile_StillPersistsTheNewRange()
    {
        var highlight = SetUpExisting(1, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        _fileSystem.DeleteFile(ClipPath);

        await _service.UpdateAsync(1, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20), null, null);

        Assert.Equal(TimeSpan.FromSeconds(5), highlight.StartTime);
        Assert.Equal(TimeSpan.FromSeconds(20), highlight.EndTime);
    }

    // ---- Tags, rating, favourite ----

    [Fact]
    public async Task AddTagAsync_AddsOnceAndIsIdempotent()
    {
        var highlight = SetUpExisting(1, TimeSpan.Zero, TimeSpan.FromSeconds(10));

        await _service.AddTagAsync(1, 5);
        await _service.AddTagAsync(1, 5);

        Assert.Single(highlight.HighlightTags);
        _highlightRepo.Verify(
            r => r.UpdateAsync(highlight, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveTagAsync_UnknownTag_IsANoOp()
    {
        var highlight = SetUpExisting(1, TimeSpan.Zero, TimeSpan.FromSeconds(10));

        await _service.RemoveTagAsync(1, 5);

        Assert.Empty(highlight.HighlightTags);
        _highlightRepo.Verify(
            r => r.UpdateAsync(It.IsAny<Highlight>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveTagAsync_ExistingTag_IsRemoved()
    {
        var highlight = SetUpExisting(1, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        await _service.AddTagAsync(1, 5);

        await _service.RemoveTagAsync(1, 5);

        Assert.Empty(highlight.HighlightTags);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public async Task SetRatingAsync_OutOfRange_Throws(int rating)
    {
        SetUpExisting(1, TimeSpan.Zero, TimeSpan.FromSeconds(10));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.SetRatingAsync(1, rating));
    }

    [Fact]
    public async Task SetRatingAsync_ValidRating_IsPersisted()
    {
        var highlight = SetUpExisting(1, TimeSpan.Zero, TimeSpan.FromSeconds(10));

        await _service.SetRatingAsync(1, 5);

        Assert.Equal(5, highlight.Rating);
    }

    [Fact]
    public async Task ToggleFavoriteAsync_FlipsTheFlag()
    {
        var highlight = SetUpExisting(1, TimeSpan.Zero, TimeSpan.FromSeconds(10));

        await _service.ToggleFavoriteAsync(1);
        Assert.True(highlight.IsFavorite);

        await _service.ToggleFavoriteAsync(1);
        Assert.False(highlight.IsFavorite);
    }
}
