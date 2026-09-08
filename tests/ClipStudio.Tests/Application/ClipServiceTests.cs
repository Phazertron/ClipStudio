using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Application.Services;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;
using ClipStudio.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClipStudio.Tests.Application;

/// <summary>
/// Unit tests for <see cref="ClipService"/> using mocked repositories.
/// </summary>
public sealed class ClipServiceTests
{
    private readonly Mock<IClipRepository> _clipRepoMock = new();
    private readonly Mock<ITagRepository> _tagRepoMock = new();
    private readonly Mock<IRecycleBinService> _recycleBinMock = new();
    private readonly Mock<ITranscriptionRepository> _transcriptionRepoMock = new();
    private readonly ClipService _service;

    public ClipServiceTests()
    {
        _transcriptionRepoMock
            .Setup(r => r.DeleteByClipIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        _service = new ClipService(
            _clipRepoMock.Object,
            _tagRepoMock.Object,
            _recycleBinMock.Object,
            _transcriptionRepoMock.Object,
            NullLogger<ClipService>.Instance);
    }

    private static Clip MakeClip(int id, ClipStatus status = ClipStatus.Unreviewed) => new()
    {
        Id = id,
        FileName = $"Clip{id}.mp4",
        FilePath = $"/clips/Clip{id}.mp4",
        Status = status,
        Duration = TimeSpan.FromMinutes(2),
        CreatedAt = DateTime.UtcNow,
        ImportedAt = DateTime.UtcNow,
        ClipTags = [],
        Highlights = []
    };

    [Fact]
    public async Task SetRatingAsync_ValidRating_UpdatesClip()
    {
        var clip = MakeClip(1);
        _clipRepoMock.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(clip);

        await _service.SetRatingAsync(1, 4);

        Assert.Equal(4, clip.Rating);
        _clipRepoMock.Verify(r => r.UpdateAsync(clip, default), Times.Once);
    }

    [Fact]
    public async Task SetRatingAsync_OutOfRange_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.SetRatingAsync(1, 6));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.SetRatingAsync(1, -1));
    }

    [Fact]
    public async Task ToggleFavouriteAsync_UnfavouritedClip_SetsFavouriteTrue()
    {
        var clip = MakeClip(1);
        clip.IsFavourite = false;
        _clipRepoMock.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(clip);

        await _service.ToggleFavouriteAsync(1);

        Assert.True(clip.IsFavourite);
    }

    [Fact]
    public async Task ToggleFavouriteAsync_FavouritedClip_SetsFavouriteFalse()
    {
        var clip = MakeClip(1);
        clip.IsFavourite = true;
        _clipRepoMock.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(clip);

        await _service.ToggleFavouriteAsync(1);

        Assert.False(clip.IsFavourite);
    }

    [Fact]
    public async Task ConfirmGameTagAsync_ClearsGameNameAndAddsTag()
    {
        // Auto-review policy was moved to the ViewModel layer (DEC-031 / Round-6 item C).
        // ClipService.ConfirmGameTagAsync now only clears SuggestedGameName and adds the tag;
        // the caller is responsible for transitioning status if AutoMarkReviewedOnTagAdd is set.
        var clip = MakeClip(1);
        clip.SuggestedGameName = "Apex Legends";
        clip.Status = ClipStatus.Unreviewed;
        _clipRepoMock.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(clip);

        await _service.ConfirmGameTagAsync(1, 42);

        Assert.Null(clip.SuggestedGameName);
        Assert.Equal(ClipStatus.Unreviewed, clip.Status); // Status unchanged — VM decides
        // Tag insertion is now done via AddClipTagAsync (direct row insert) rather than
        // modifying clip.ClipTags in memory, so we verify the repository call instead.
        _clipRepoMock.Verify(r => r.AddClipTagAsync(1, 42, default), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_DelegatesFilteringToRepository()
    {
        var clips = new List<Clip> { MakeClip(1), MakeClip(2) };
        ClipSearchQuery? forwarded = null;

        _clipRepoMock
            .Setup(r => r.SearchAsync(It.IsAny<ClipSearchQuery>(), default))
            .Callback<ClipSearchQuery, CancellationToken>((q, _) => forwarded = q)
            .ReturnsAsync(clips);

        var query = new ClipSearchQuery { Status = ClipStatus.Unreviewed, MinRating = 3 };
        var results = await _service.SearchAsync(query);

        Assert.Equal(2, results.Count);
        Assert.NotNull(forwarded);
        Assert.Equal(ClipStatus.Unreviewed, forwarded!.Status);
        Assert.Equal(3, forwarded.MinRating);
        _clipRepoMock.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_ExpandsTagDescendants_BeforeQueryingRepository()
    {
        ClipSearchQuery? forwarded = null;

        _clipRepoMock
            .Setup(r => r.SearchAsync(It.IsAny<ClipSearchQuery>(), default))
            .Callback<ClipSearchQuery, CancellationToken>((q, _) => forwarded = q)
            .ReturnsAsync([]);

        _tagRepoMock
            .Setup(r => r.GetDescendantIdsAsync(7, default))
            .ReturnsAsync(new List<int> { 8, 9 });

        var query = new ClipSearchQuery { TagIds = [7], IncludeTagDescendants = true };
        await _service.SearchAsync(query);

        Assert.NotNull(forwarded);
        Assert.Equal([7, 8, 9], forwarded!.TagIds.OrderBy(id => id));
        Assert.False(forwarded.IncludeTagDescendants);

        // The caller's query must not be mutated by the expansion.
        Assert.Equal([7], query.TagIds);
        Assert.True(query.IncludeTagDescendants);
    }

    [Fact]
    public async Task SearchAsync_TextFilter_MatchesFileName()
    {
        var match = MakeClip(1); match.FileName = "KillerCombo_clip.mp4";
        var noMatch = MakeClip(2); noMatch.FileName = "random.mp4";

        _clipRepoMock
            .Setup(r => r.SearchAsync(It.IsAny<ClipSearchQuery>(), default))
            .ReturnsAsync([match, noMatch]);

        var query = new ClipSearchQuery { SearchText = "KillerCombo" };
        var results = await _service.SearchAsync(query);

        Assert.Single(results);
        Assert.Equal("KillerCombo_clip.mp4", results[0].FileName);
    }

    [Fact]
    public async Task SearchAsync_TextFilter_MatchesNotesAndHighlightLabels()
    {
        var byNotes = MakeClip(1); byNotes.Notes = "clutch ACE round";
        var byLabel = MakeClip(2);
        byLabel.Highlights = [new Highlight { Id = 1, ClipId = 2, Label = "Triple ace" }];
        var noMatch = MakeClip(3);

        _clipRepoMock
            .Setup(r => r.SearchAsync(It.IsAny<ClipSearchQuery>(), default))
            .ReturnsAsync([byNotes, byLabel, noMatch]);

        var results = await _service.SearchAsync(new ClipSearchQuery { SearchText = "ace" });

        Assert.Equal([1, 2], results.Select(c => c.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task SearchAsync_SearchCaptions_IncludesCaptionMatchesOnly_WhenEnabled()
    {
        var captionMatch = MakeClip(1);
        var noMatch = MakeClip(2);

        _clipRepoMock
            .Setup(r => r.SearchAsync(It.IsAny<ClipSearchQuery>(), default))
            .ReturnsAsync([captionMatch, noMatch]);
        _transcriptionRepoMock
            .Setup(r => r.SearchClipIdsBySegmentTextAsync("headshot", default))
            .ReturnsAsync([1]);

        var withCaptions = await _service.SearchAsync(
            new ClipSearchQuery { SearchText = "headshot", SearchCaptions = true });

        Assert.Single(withCaptions);
        Assert.Equal(1, withCaptions[0].Id);

        var withoutCaptions = await _service.SearchAsync(
            new ClipSearchQuery { SearchText = "headshot", SearchCaptions = false });

        Assert.Empty(withoutCaptions);
        _transcriptionRepoMock.Verify(
            r => r.SearchClipIdsBySegmentTextAsync("headshot", default), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_Throws_WhenCalledViaSetStatus()
    {
        _clipRepoMock.Setup(r => r.GetByIdAsync(999, default)).ReturnsAsync((Clip?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.SetStatusAsync(999, ClipStatus.Archived));
    }
}
