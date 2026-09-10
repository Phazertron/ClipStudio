using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Application.Services;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;
using ClipStudio.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClipStudio.Tests.Application;

/// <summary>
/// Unit tests for <see cref="SearchService"/>.
/// </summary>
public sealed class SearchServiceTests
{
    private readonly Mock<IClipService> _clips = new();
    private readonly Mock<ITagService> _tags = new();
    private readonly Mock<IPlayerService> _players = new();
    private readonly Mock<IHighlightRepository> _highlights = new();
    private readonly Mock<ITranscriptionRepository> _transcriptions = new();
    private readonly SearchService _service;

    public SearchServiceTests()
    {
        _clips.Setup(x => x.SearchAsync(It.IsAny<ClipSearchQuery>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([]);
        _tags.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _players.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _highlights.Setup(x => x.SearchByLabelAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([]);
        _transcriptions.Setup(x => x.SearchSegmentsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync([]);

        _service = new SearchService(
            _clips.Object, _tags.Object, _players.Object,
            _highlights.Object, _transcriptions.Object,
            NullLogger<SearchService>.Instance);
    }

    private static Clip Clip(int id, string name) => new()
    {
        Id = id, FileName = name, FilePath = $"/clips/{name}", CreatedAt = DateTime.UtcNow,
    };

    private void WithClips(params Clip[] clips) =>
        _clips.Setup(x => x.SearchAsync(It.IsAny<ClipSearchQuery>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(clips);

    private void WithTags(params Tag[] tags) =>
        _tags.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(tags);

    // ---- Term handling ----

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    public async Task ATermTooShortSearchesNothingAtAll(string term)
    {
        // A single character matches most of the library: slow to produce, useless to read.
        Assert.Empty(await _service.SearchAsync(term));

        _clips.Verify(x => x.SearchAsync(It.IsAny<ClipSearchQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TheTermIsTrimmedBeforeSearching()
    {
        WithClips(Clip(1, "warframe.mp4"));

        var groups = await _service.SearchAsync("  warframe  ");

        Assert.NotEmpty(groups);
        _clips.Verify(x => x.SearchAsync(
            It.Is<ClipSearchQuery>(q => q.SearchText == "warframe"), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- Grouping ----

    [Fact]
    public async Task AGroupWithNoMatchesIsNotShown()
    {
        // A heading with nothing under it is noise.
        WithClips(Clip(1, "a.mp4"));

        var groups = await _service.SearchAsync("a.mp4");

        Assert.Single(groups);
        Assert.Equal(SearchResultKind.Clip, groups[0].Kind);
    }

    [Fact]
    public async Task GamesAndTagsAreSeparateGroups()
    {
        // Filtering by a game is a different act from filtering by a general tag, and the two are
        // easy to confuse under a shared heading.
        WithTags(
            new Tag { Id = 1, Name = "clutch", Type = TagType.General },
            new Tag { Id = 2, Name = "Clustertruck", Type = TagType.Game });

        var groups = await _service.SearchAsync("clu");

        Assert.Contains(groups, g => g.Kind == SearchResultKind.Game && g.Items[0].Title == "Clustertruck");
        Assert.Contains(groups, g => g.Kind == SearchResultKind.Tag && g.Items[0].Title == "clutch");
    }

    [Fact]
    public async Task EachGroupIsCappedAndReportsHowManyMoreThereAre()
    {
        // One kind with hundreds of matches must not bury the others.
        WithClips(Enumerable.Range(1, 12).Select(i => Clip(i, $"clip{i}.mp4")).ToArray());

        var group = Assert.Single(await _service.SearchAsync("clip"));

        Assert.Equal(5, group.Items.Count);
        Assert.Equal(12, group.TotalCount);
        Assert.True(group.HasMore);
        Assert.Equal(7, group.MoreCount);
    }

    // ---- Result shape ----

    [Fact]
    public async Task AHighlightResultCarriesItsClipAndStart()
    {
        _highlights.Setup(x => x.SearchByLabelAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([new HighlightRangeSnapshot(
                       9, 3, "clutch", "clip.mp4",
                       TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(120), TimeSpan.FromMinutes(5))]);

        var group = Assert.Single(await _service.SearchAsync("clu"));
        var item = Assert.Single(group.Items);

        Assert.Equal("clutch", item.Title);
        Assert.Equal("clip.mp4", item.Subtitle);
        Assert.Equal(3, item.ClipId);
        Assert.Equal(TimeSpan.FromSeconds(90), item.SeekTo);
    }

    [Fact]
    public async Task AnUnlabelledHighlightStillReadsAsSomething()
    {
        _highlights.Setup(x => x.SearchByLabelAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([new HighlightRangeSnapshot(
                       9, 3, null, "clip.mp4",
                       TimeSpan.Zero, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5))]);

        var item = Assert.Single(Assert.Single(await _service.SearchAsync("clu")).Items);

        Assert.Equal("(unlabelled)", item.Title);
    }

    // ---- Captions ----

    [Fact]
    public async Task CaptionsAreNotSearchedUnlessAskedFor()
    {
        // The most expensive of the queries, and useless until clips have been transcribed.
        await _service.SearchAsync("term");

        _transcriptions.Verify(
            x => x.SearchSegmentsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ACaptionResultSaysWhatWasSaidAndWhere()
    {
        _transcriptions.Setup(x => x.SearchSegmentsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync([new CaptionMatch(4, "clip.mp4", TimeSpan.FromSeconds(90), " nice shot ")]);

        var group = Assert.Single(await _service.SearchAsync("nice", includeCaptions: true));
        var item = Assert.Single(group.Items);

        Assert.Equal("nice shot", item.Title);
        Assert.Contains("clip.mp4", item.Subtitle);
        Assert.Contains("1:30", item.Subtitle);
        Assert.Equal(TimeSpan.FromSeconds(90), item.SeekTo);
    }

    // ---- Resilience ----

    [Fact]
    public async Task OneFailingQueryDoesNotEmptyTheOthers()
    {
        // A missing transcript table should not stop you finding a clip by name.
        WithClips(Clip(1, "warframe.mp4"));
        _transcriptions.Setup(x => x.SearchSegmentsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                       .ThrowsAsync(new InvalidOperationException("no such table"));

        var groups = await _service.SearchAsync("warframe", includeCaptions: true);

        Assert.Contains(groups, g => g.Kind == SearchResultKind.Clip);
    }

    [Fact]
    public async Task CancellationIsNotSwallowed()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _clips.Setup(x => x.SearchAsync(It.IsAny<ClipSearchQuery>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.SearchAsync("warframe", false, cts.Token));
    }
}
