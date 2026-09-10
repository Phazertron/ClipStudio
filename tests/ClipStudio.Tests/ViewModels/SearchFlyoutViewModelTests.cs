using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.UI.ViewModels;
using Moq;

namespace ClipStudio.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="SearchFlyoutViewModel"/>.
/// </summary>
/// <remarks>
/// The keyboard behaviour is what these are really for. The tag picker needed three separate fixes
/// to get its key handling right, and none would have been caught by a compiled binding - so the
/// logic lives here, free of Avalonia, where it can be driven directly.
/// </remarks>
public sealed class SearchFlyoutViewModelTests
{
    private readonly Mock<ISearchService> _search = new();
    private bool _captionsEnabled;

    private SearchFlyoutViewModel Build()
        => new(_search.Object, () => _captionsEnabled);

    private static SearchResultItem Clip(string name, int id) =>
        new(SearchResultKind.Clip, name, ClipId: id);

    private static SearchResultItem Tag(string name, int id) =>
        new(SearchResultKind.Tag, name, EntityId: id);

    private void Returns(params SearchResultGroup[] groups) =>
        _search.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(groups);

    private static SearchResultGroup Group(SearchResultKind kind, string title, params SearchResultItem[] items)
        => new(kind, title, items, items.Length);

    // ---- Searching ----

    [Fact]
    public async Task ASearchOpensTheFlyoutWithItsGroups()
    {
        Returns(
            Group(SearchResultKind.Clip, "Clips", Clip("a.mp4", 1)),
            Group(SearchResultKind.Tag, "Tags", Tag("clutch", 2)));

        var vm = Build();
        await vm.SearchAsync("clu");

        Assert.True(vm.IsOpen);
        Assert.Equal(2, vm.Groups.Count);
        Assert.Equal(2, vm.FlatResults.Count);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public async Task NothingIsPreselected()
    {
        // The first Down should land on the first result, and Enter with nothing chosen should
        // fall through to the search box rather than opening whatever happened to be first.
        Returns(Group(SearchResultKind.Clip, "Clips", Clip("a.mp4", 1)));

        var vm = Build();
        await vm.SearchAsync("a");

        Assert.Equal(-1, vm.SelectedIndex);
    }

    [Fact]
    public async Task AnEmptyResultOpensAndSaysSo()
    {
        Returns();

        var vm = Build();
        await vm.SearchAsync("zzz");

        Assert.True(vm.IsOpen);
        Assert.True(vm.IsEmpty);
        Assert.Empty(vm.FlatResults);
    }

    [Fact]
    public async Task ATermTooShortToSearchClosesInsteadOfQuerying()
    {
        var vm = Build();
        await vm.QueueSearchAsync("a");

        Assert.False(vm.IsOpen);
        _search.Verify(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TypingAgainSupersedesThePendingSearch()
    {
        // A slow query for an older term must never overwrite a newer one's results.
        Returns(Group(SearchResultKind.Clip, "Clips", Clip("a.mp4", 1)));
        var vm = Build();

        var first = vm.QueueSearchAsync("warfr");
        var second = vm.QueueSearchAsync("warframe");
        await Task.WhenAll(first, second);

        _search.Verify(
            x => x.SearchAsync("warframe", It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _search.Verify(
            x => x.SearchAsync("warfr", It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CaptionsAreSearchedOnlyWhenTheSettingIsOn()
    {
        Returns();
        _captionsEnabled = true;

        var vm = Build();
        await vm.SearchAsync("term");

        _search.Verify(x => x.SearchAsync("term", true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AFailedSearchClosesRatherThanShowingStaleResults()
    {
        Returns(Group(SearchResultKind.Clip, "Clips", Clip("a.mp4", 1)));
        var vm = Build();
        await vm.SearchAsync("a");

        _search.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("no database"));
        await vm.SearchAsync("b");

        Assert.False(vm.IsOpen);
        Assert.Empty(vm.FlatResults);
    }

    // ---- Keyboard navigation ----

    [Fact]
    public async Task DownMovesThroughEveryResultAcrossGroups()
    {
        // Arrow keys step from the last clip straight to the first tag without stopping on a
        // heading, which is why navigation runs over the flattened list.
        Returns(
            Group(SearchResultKind.Clip, "Clips", Clip("a.mp4", 1), Clip("b.mp4", 2)),
            Group(SearchResultKind.Tag, "Tags", Tag("clutch", 3)));

        var vm = Build();
        await vm.SearchAsync("c");

        Assert.True(vm.MoveNext());
        Assert.Equal(0, vm.SelectedIndex);
        vm.MoveNext();
        vm.MoveNext();
        Assert.Equal(2, vm.SelectedIndex);
        Assert.Equal(SearchResultKind.Tag, vm.FlatResults[vm.SelectedIndex].Item.Kind);
    }

    [Fact]
    public async Task DownWrapsAtTheEnd()
    {
        Returns(Group(SearchResultKind.Clip, "Clips", Clip("a.mp4", 1), Clip("b.mp4", 2)));
        var vm = Build();
        await vm.SearchAsync("a");

        vm.MoveNext();
        vm.MoveNext();
        vm.MoveNext();

        Assert.Equal(0, vm.SelectedIndex);
    }

    [Fact]
    public async Task UpFromNothingSelectedLandsOnTheLastResult()
    {
        Returns(Group(SearchResultKind.Clip, "Clips", Clip("a.mp4", 1), Clip("b.mp4", 2)));
        var vm = Build();
        await vm.SearchAsync("a");

        Assert.True(vm.MovePrevious());
        Assert.Equal(1, vm.SelectedIndex);
    }

    [Fact]
    public void ArrowKeysAreNotUsedWhenTheFlyoutIsClosed()
    {
        // Reporting the press unused is what lets it reach the control underneath.
        var vm = Build();

        Assert.False(vm.MoveNext());
        Assert.False(vm.MovePrevious());
        Assert.False(vm.ActivateSelected());
        Assert.False(vm.HandleEscape());
    }

    [Fact]
    public async Task ArrowKeysAreNotUsedWhenThereAreNoResults()
    {
        Returns();
        var vm = Build();
        await vm.SearchAsync("zzz");

        Assert.True(vm.IsOpen);
        Assert.False(vm.MoveNext());
    }

    [Fact]
    public async Task EscapeClosesAndReportsThePressUsed()
    {
        Returns(Group(SearchResultKind.Clip, "Clips", Clip("a.mp4", 1)));
        var vm = Build();
        await vm.SearchAsync("a");

        Assert.True(vm.HandleEscape());
        Assert.False(vm.IsOpen);
    }

    // ---- Activation ----

    [Fact]
    public async Task EnterWithNothingSelectedIsNotUsed()
    {
        // So it still reaches the search box and runs an ordinary filter.
        Returns(Group(SearchResultKind.Clip, "Clips", Clip("a.mp4", 1)));
        var vm = Build();
        await vm.SearchAsync("a");

        var opened = false;
        vm.ClipRequested = (_, _) => opened = true;

        Assert.False(vm.ActivateSelected());
        Assert.False(opened);
        Assert.True(vm.IsOpen);
    }

    [Fact]
    public async Task EnterOnAClipOpensItAndClosesTheFlyout()
    {
        Returns(Group(SearchResultKind.Clip, "Clips", Clip("a.mp4", 7)));
        var vm = Build();
        await vm.SearchAsync("a");

        int? opened = null;
        vm.ClipRequested = (id, _) => opened = id;

        vm.MoveNext();
        Assert.True(vm.ActivateSelected());

        Assert.Equal(7, opened);
        Assert.False(vm.IsOpen);
    }

    [Fact]
    public async Task EnterOnAHighlightOpensItsClipAtItsStart()
    {
        Returns(Group(SearchResultKind.Highlight, "Highlights",
            new SearchResultItem(SearchResultKind.Highlight, "clutch", "a.mp4",
                ClipId: 7, SeekTo: TimeSpan.FromSeconds(90))));

        var vm = Build();
        await vm.SearchAsync("clu");

        (int Id, TimeSpan? Seek)? opened = null;
        vm.ClipRequested = (id, seek) => opened = (id, seek);

        vm.MoveNext();
        vm.ActivateSelected();

        Assert.Equal(7, opened?.Id);
        Assert.Equal(TimeSpan.FromSeconds(90), opened?.Seek);
    }

    [Fact]
    public async Task EnterOnACaptionOpensItsClipAtTheSpokenLine()
    {
        Returns(Group(SearchResultKind.Caption, "Captions",
            new SearchResultItem(SearchResultKind.Caption, "nice shot", "a.mp4 at 1:30",
                ClipId: 4, SeekTo: TimeSpan.FromSeconds(90))));

        var vm = Build();
        await vm.SearchAsync("nice");

        (int Id, TimeSpan? Seek)? opened = null;
        vm.ClipRequested = (id, seek) => opened = (id, seek);

        vm.MoveNext();
        vm.ActivateSelected();

        Assert.Equal(4, opened?.Id);
        Assert.Equal(TimeSpan.FromSeconds(90), opened?.Seek);
    }

    [Fact]
    public async Task EnterOnATagAppliesItAsAFilterRatherThanOpeningAClip()
    {
        Returns(Group(SearchResultKind.Tag, "Tags", Tag("clutch", 3)));
        var vm = Build();
        await vm.SearchAsync("clu");

        int? filtered = null;
        var clipOpened = false;
        vm.TagFilterRequested = id => filtered = id;
        vm.ClipRequested = (_, _) => clipOpened = true;

        vm.MoveNext();
        vm.ActivateSelected();

        Assert.Equal(3, filtered);
        Assert.False(clipOpened);
    }

    [Fact]
    public async Task EnterOnAGameUsesTheGameFilterNotTheTagFilter()
    {
        Returns(Group(SearchResultKind.Game, "Games",
            new SearchResultItem(SearchResultKind.Game, "Warframe", EntityId: 9)));

        var vm = Build();
        await vm.SearchAsync("war");

        int? filtered = null;
        var generalTagFiltered = false;
        vm.GameFilterRequested = id => filtered = id;
        vm.TagFilterRequested = _ => generalTagFiltered = true;

        vm.MoveNext();
        vm.ActivateSelected();

        Assert.Equal(9, filtered);

        // A game routed into the general tag filter would silently match nothing.
        Assert.False(generalTagFiltered);
    }

    [Fact]
    public async Task EnterOnAPlayerAppliesThePlayerFilter()
    {
        Returns(Group(SearchResultKind.Player, "Players",
            new SearchResultItem(SearchResultKind.Player, "Jane", EntityId: 5)));

        var vm = Build();
        await vm.SearchAsync("jan");

        int? filtered = null;
        vm.PlayerFilterRequested = id => filtered = id;

        vm.MoveNext();
        vm.ActivateSelected();

        Assert.Equal(5, filtered);
    }

    [Fact]
    public async Task TheHighlightIsCarriedOnTheRowSoItCanBeSeen()
    {
        // SelectedIndex alone drives nothing visible: the results are drawn as nested lists, and a
        // row cannot ask whether it is the nth item overall.
        Returns(Group(SearchResultKind.Clip, "Clips", Clip("a.mp4", 1), Clip("b.mp4", 2)));
        var vm = Build();
        await vm.SearchAsync("a");

        Assert.All(vm.FlatResults, r => Assert.False(r.IsSelected));

        vm.MoveNext();
        Assert.True(vm.FlatResults[0].IsSelected);
        Assert.False(vm.FlatResults[1].IsSelected);

        vm.MoveNext();
        Assert.False(vm.FlatResults[0].IsSelected);
        Assert.True(vm.FlatResults[1].IsSelected);
    }

    [Fact]
    public async Task ClickingAResultActsTheSameAsChoosingItWithEnter()
    {
        Returns(Group(SearchResultKind.Clip, "Clips", Clip("a.mp4", 7)));
        var vm = Build();
        await vm.SearchAsync("a");

        int? opened = null;
        vm.ClipRequested = (id, _) => opened = id;

        vm.ActivateCommand.Execute(vm.FlatResults[0]);

        Assert.Equal(7, opened);
        Assert.False(vm.IsOpen);
    }
}
