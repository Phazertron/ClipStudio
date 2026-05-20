using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Application.Services;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;
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
    public async Task SearchAsync_StatusFilter_AppliedCorrectly()
    {
        var clips = new List<Clip>
        {
            MakeClip(1, ClipStatus.Unreviewed),
            MakeClip(2, ClipStatus.Reviewed),
            MakeClip(3, ClipStatus.Unreviewed)
        };

        _clipRepoMock.Setup(r => r.GetAllAsync(default)).ReturnsAsync(clips);
        _tagRepoMock.Setup(r => r.GetDescendantIdsAsync(It.IsAny<int>(), default))
                    .ReturnsAsync(new List<int>());

        var query = new ClipSearchQuery { Status = ClipStatus.Unreviewed };
        var results = await _service.SearchAsync(query);

        Assert.Equal(2, results.Count);
        Assert.All(results, c => Assert.Equal(ClipStatus.Unreviewed, c.Status));
    }

    [Fact]
    public async Task SearchAsync_MinRatingFilter_ExcludesLowRatedClips()
    {
        var low = MakeClip(1); low.Rating = 1;
        var high = MakeClip(2); high.Rating = 4;

        _clipRepoMock.Setup(r => r.GetAllAsync(default)).ReturnsAsync([low, high]);
        _tagRepoMock.Setup(r => r.GetDescendantIdsAsync(It.IsAny<int>(), default))
                    .ReturnsAsync(new List<int>());

        var query = new ClipSearchQuery { MinRating = 3 };
        var results = await _service.SearchAsync(query);

        Assert.Single(results);
        Assert.Equal(4, results[0].Rating);
    }

    [Fact]
    public async Task SearchAsync_TextFilter_MatchesFileName()
    {
        var match = MakeClip(1); match.FileName = "KillerCombo_clip.mp4";
        var noMatch = MakeClip(2); noMatch.FileName = "random.mp4";

        _clipRepoMock.Setup(r => r.GetAllAsync(default)).ReturnsAsync([match, noMatch]);
        _tagRepoMock.Setup(r => r.GetDescendantIdsAsync(It.IsAny<int>(), default))
                    .ReturnsAsync(new List<int>());

        var query = new ClipSearchQuery { SearchText = "KillerCombo" };
        var results = await _service.SearchAsync(query);

        Assert.Single(results);
        Assert.Equal("KillerCombo_clip.mp4", results[0].FileName);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_Throws_WhenCalledViaSetStatus()
    {
        _clipRepoMock.Setup(r => r.GetByIdAsync(999, default)).ReturnsAsync((Clip?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.SetStatusAsync(999, ClipStatus.Archived));
    }
}
