using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Models;
using ClipStudio.Tests.Fakes;
using ClipStudio.UI.ViewModels;
using Moq;

namespace ClipStudio.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="RelatedClipsViewModel"/>, the panel that shows and creates clip links.
/// </summary>
public sealed class RelatedClipsViewModelTests
{
    private readonly FakeRelatedClipsHost _host = new();
    private readonly Mock<IClipLinkService> _links = new();
    private readonly Mock<IClipService> _clips = new();
    private readonly RelatedClipsViewModel _vm;

    public RelatedClipsViewModelTests()
    {
        _links.Setup(x => x.GetRelatedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([]);
        _clips.Setup(x => x.SearchAsync(It.IsAny<ClipSearchQuery>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([]);

        _vm = new RelatedClipsViewModel(_host, _links.Object, _clips.Object);
    }

    private static Clip Clip(int id, string name) => new()
    {
        Id = id, FileName = name, FilePath = $"/clips/{name}", Duration = TimeSpan.FromMinutes(2),
    };

    private void Related(params RelatedClip[] related) =>
        _links.Setup(x => x.GetRelatedAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(related);

    // ---- Loading ----

    [Fact]
    public async Task AClipWithNoLinksSaysSo()
    {
        await _vm.LoadAsync();

        Assert.Empty(_vm.Related);
        Assert.True(_vm.HasNoLinks);
    }

    [Fact]
    public async Task ShowsTheLabelTheServiceResolved()
    {
        // The panel does not work out the wording itself - which end you are looking from decides
        // it, and the service already knows.
        Related(new RelatedClip(9, Clip(2, "part1.mp4"), ClipLinkType.Sequel, "Prequel", null));

        await _vm.LoadAsync();

        var row = Assert.Single(_vm.Related);
        Assert.Equal("Prequel", row.RelationshipLabel);
        Assert.Equal(9, row.LinkId);
        Assert.Equal(2, row.ClipId);
        Assert.False(_vm.HasNoLinks);
    }

    [Fact]
    public async Task LoadsNothingWhenNoClipIsOpen()
    {
        _host.OpenClipId = null;

        await _vm.LoadAsync();

        Assert.Empty(_vm.Related);
        _links.Verify(x => x.GetRelatedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReportsAFailureRatherThanLeavingThePanelBlank()
    {
        _links.Setup(x => x.GetRelatedAsync(1, It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("no database"));

        await _vm.LoadAsync();

        Assert.NotNull(_vm.ErrorMessage);
        Assert.False(_vm.IsBusy);
    }

    // ---- Opening and unlinking ----

    [Fact]
    public async Task OpeningARelatedClipAsksTheHostToNavigate()
    {
        Related(new RelatedClip(9, Clip(2, "other.mp4"), ClipLinkType.SameMoment, "Same moment", null));
        await _vm.LoadAsync();

        _vm.OpenCommand.Execute(_vm.Related[0]);

        Assert.Equal([2], _host.OpenedClipIds);
    }

    [Fact]
    public async Task UnlinkingRemovesTheRowWithoutReloading()
    {
        Related(new RelatedClip(9, Clip(2, "other.mp4"), ClipLinkType.SameMoment, "Same moment", null));
        await _vm.LoadAsync();

        await _vm.UnlinkCommand.ExecuteAsync(_vm.Related[0]);

        _links.Verify(x => x.UnlinkAsync(9, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(_vm.Related);
        Assert.True(_vm.HasNoLinks);
    }

    [Fact]
    public async Task AFailedUnlinkKeepsTheRowAndSaysWhy()
    {
        // Removing the row on a failed delete would tell the user the link is gone when it is not.
        Related(new RelatedClip(9, Clip(2, "other.mp4"), ClipLinkType.SameMoment, "Same moment", null));
        await _vm.LoadAsync();
        _links.Setup(x => x.UnlinkAsync(9, It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("locked"));

        await _vm.UnlinkCommand.ExecuteAsync(_vm.Related[0]);

        Assert.Single(_vm.Related);
        Assert.NotNull(_vm.ErrorMessage);
    }

    // ---- The picker ----

    [Fact]
    public void ClosingThePickerClearsWhatWasTypedIntoIt()
    {
        _vm.TogglePickerCommand.Execute(null);
        _vm.SearchText  = "warframe";
        _vm.NewLinkNote = "same fight";

        _vm.TogglePickerCommand.Execute(null);

        Assert.False(_vm.IsPickerOpen);
        Assert.Equal(string.Empty, _vm.SearchText);
        Assert.Equal(string.Empty, _vm.NewLinkNote);
    }

    [Fact]
    public async Task SearchingOffersMatchesToLinkTo()
    {
        _clips.Setup(x => x.SearchAsync(It.IsAny<ClipSearchQuery>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([Clip(2, "a.mp4"), Clip(3, "b.mp4")]);
        _vm.SearchText = "replay";

        await _vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal(2, _vm.SearchResults.Count);
    }

    [Fact]
    public async Task SearchNeverOffersTheClipYouAreLookingAt()
    {
        // A clip cannot be linked to itself, so offering it would only produce an error.
        _clips.Setup(x => x.SearchAsync(It.IsAny<ClipSearchQuery>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([Clip(1, "open.mp4"), Clip(2, "other.mp4")]);
        _vm.SearchText = "replay";

        await _vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal([2], _vm.SearchResults.Select(r => r.ClipId));
    }

    [Fact]
    public async Task SearchNeverOffersAClipThatIsAlreadyLinked()
    {
        // Choosing one would be a no-op the user could not see the result of.
        Related(new RelatedClip(9, Clip(2, "other.mp4"), ClipLinkType.SameMoment, "Same moment", null));
        await _vm.LoadAsync();

        _clips.Setup(x => x.SearchAsync(It.IsAny<ClipSearchQuery>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([Clip(2, "other.mp4"), Clip(3, "new.mp4")]);
        _vm.SearchText = "replay";

        await _vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal([3], _vm.SearchResults.Select(r => r.ClipId));
    }

    [Fact]
    public async Task AnEmptySearchAsksNothing()
    {
        _vm.SearchText = "   ";

        await _vm.SearchCommand.ExecuteAsync(null);

        Assert.Empty(_vm.SearchResults);
        _clips.Verify(x => x.SearchAsync(It.IsAny<ClipSearchQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Creating a link ----

    [Fact]
    public async Task LinkingUsesTheChosenTypeAndNoteThenClosesThePicker()
    {
        _clips.Setup(x => x.SearchAsync(It.IsAny<ClipSearchQuery>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([Clip(2, "other.mp4")]);
        _vm.TogglePickerCommand.Execute(null);
        _vm.SearchText = "replay";
        await _vm.SearchCommand.ExecuteAsync(null);

        _vm.SelectedLinkType = RelatedClipsViewModel.LinkTypeOptions.First(o => o.Type == ClipLinkType.Reaction);
        _vm.NewLinkNote      = "my face";

        await _vm.LinkToCommand.ExecuteAsync(_vm.SearchResults[0]);

        _links.Verify(x => x.LinkAsync(1, 2, ClipLinkType.Reaction, "my face", It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(_vm.IsPickerOpen);
        Assert.Empty(_vm.SearchResults);
    }

    [Fact]
    public async Task LinkingReloadsSoTheNewLinkAppears()
    {
        _clips.Setup(x => x.SearchAsync(It.IsAny<ClipSearchQuery>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([Clip(2, "other.mp4")]);
        _vm.SearchText = "replay";
        await _vm.SearchCommand.ExecuteAsync(null);

        await _vm.LinkToCommand.ExecuteAsync(_vm.SearchResults[0]);

        // Once for the link, once for the reload that follows it.
        _links.Verify(x => x.GetRelatedAsync(1, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task OpeningThePickerForAKnownClipSkipsTheSearch()
    {
        // The path "import and link" uses: the clip is already known, so the user should not have
        // to search for what they just told the application about.
        _clips.Setup(x => x.GetByIdAsync(7, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Clip(7, "the duplicate.mp4"));

        await _vm.OpenPickerForAsync(7, ClipLinkType.Variant);

        Assert.True(_vm.IsPickerOpen);
        Assert.Equal(ClipLinkType.Variant, _vm.SelectedLinkType.Type);
        Assert.Equal(7, Assert.Single(_vm.SearchResults).ClipId);
        _clips.Verify(x => x.SearchAsync(It.IsAny<ClipSearchQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void EveryLinkTypeIsOfferedWithAHint()
    {
        // The names alone are ambiguous - "Variant" says nothing without the explanation.
        Assert.Equal(
            Enum.GetValues<ClipLinkType>().Length,
            RelatedClipsViewModel.LinkTypeOptions.Count);

        Assert.All(RelatedClipsViewModel.LinkTypeOptions, o =>
        {
            Assert.False(string.IsNullOrWhiteSpace(o.Label));
            Assert.False(string.IsNullOrWhiteSpace(o.Hint));
        });
    }
}
