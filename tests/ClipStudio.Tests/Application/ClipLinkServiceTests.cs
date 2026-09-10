using ClipStudio.Application.Services;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClipStudio.Tests.Application;

/// <summary>
/// Unit tests for <see cref="ClipLinkService"/>.
/// </summary>
/// <remarks>
/// The service's job beyond storage is describing a relationship from the side you are looking at
/// it from, which is what the asymmetric link types need and what the UI would otherwise have to
/// re-derive everywhere a link is shown.
/// </remarks>
public sealed class ClipLinkServiceTests
{
    private readonly Mock<IClipLinkRepository> _links = new();
    private readonly ClipLinkService _service;

    public ClipLinkServiceTests()
    {
        _service = new ClipLinkService(_links.Object, NullLogger<ClipLinkService>.Instance);
    }

    private static Clip Clip(int id, string name) => new()
    {
        Id = id, FileName = name, FilePath = $"/clips/{name}",
    };

    private ClipLink StoredLink(int id, Clip source, Clip target, ClipLinkType type, string? note = null)
    {
        var link = new ClipLink
        {
            Id           = id,
            SourceClipId = source.Id,
            SourceClip   = source,
            TargetClipId = target.Id,
            TargetClip   = target,
            LinkType     = type,
            Note         = note,
        };

        _links.Setup(x => x.GetForClipAsync(source.Id, It.IsAny<CancellationToken>()))
              .ReturnsAsync([link]);
        _links.Setup(x => x.GetForClipAsync(target.Id, It.IsAny<CancellationToken>()))
              .ReturnsAsync([link]);

        return link;
    }

    // ---- Describing a relationship from each side ----

    [Theory]
    [InlineData(ClipLinkType.SameMoment, "Same moment", "Same moment")]
    [InlineData(ClipLinkType.Variant,    "Variant",     "Variant")]
    [InlineData(ClipLinkType.Sequel,     "Sequel",      "Prequel")]
    [InlineData(ClipLinkType.Reaction,   "Reaction",    "Reacted to")]
    public void DescribesEachTypeFromBothEnds(ClipLinkType type, string fromSource, string fromTarget)
    {
        Assert.Equal(fromSource, ClipLinkService.DescribeFrom(type, fromSource: true));
        Assert.Equal(fromTarget, ClipLinkService.DescribeFrom(type, fromSource: false));
    }

    [Fact]
    public async Task TheClipYouMarkedAsASequelSeesYouAsItsPrequel()
    {
        // The case the whole inverse-wording rule exists for: showing "Sequel" on both ends would
        // say the two clips each come after the other.
        var origin = Clip(1, "part1.mp4");
        var sequel = Clip(2, "part2.mp4");
        StoredLink(10, origin, sequel, ClipLinkType.Sequel);

        var fromOrigin = Assert.Single(await _service.GetRelatedAsync(origin.Id));
        var fromSequel = Assert.Single(await _service.GetRelatedAsync(sequel.Id));

        Assert.Equal("Sequel", fromOrigin.RelationshipLabel);
        Assert.Equal("part2.mp4", fromOrigin.Clip.FileName);

        Assert.Equal("Prequel", fromSequel.RelationshipLabel);
        Assert.Equal("part1.mp4", fromSequel.Clip.FileName);
    }

    [Fact]
    public async Task ASymmetricLinkReadsTheSameFromEitherEnd()
    {
        var a = Clip(1, "a.mp4");
        var b = Clip(2, "b.mp4");
        StoredLink(10, a, b, ClipLinkType.SameMoment);

        Assert.Equal("Same moment", (await _service.GetRelatedAsync(a.Id))[0].RelationshipLabel);
        Assert.Equal("Same moment", (await _service.GetRelatedAsync(b.Id))[0].RelationshipLabel);
    }

    [Fact]
    public async Task AlwaysShowsTheOtherClipNeverTheAskingOne()
    {
        var a = Clip(1, "a.mp4");
        var b = Clip(2, "b.mp4");
        StoredLink(10, a, b, ClipLinkType.Variant);

        Assert.Equal(b.Id, (await _service.GetRelatedAsync(a.Id))[0].Clip.Id);
        Assert.Equal(a.Id, (await _service.GetRelatedAsync(b.Id))[0].Clip.Id);
    }

    [Fact]
    public async Task CarriesTheNoteAndTheLinkIdentifierThrough()
    {
        var a = Clip(1, "a.mp4");
        var b = Clip(2, "b.mp4");
        StoredLink(42, a, b, ClipLinkType.SameMoment, note: "same fight, my POV");

        var related = Assert.Single(await _service.GetRelatedAsync(a.Id));

        Assert.Equal(42, related.LinkId);
        Assert.Equal("same fight, my POV", related.Note);
    }

    // ---- Linking ----

    [Fact]
    public async Task RefusesToLinkAClipToItself()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.LinkAsync(1, 1, ClipLinkType.SameMoment));

        _links.Verify(x => x.AddAsync(It.IsAny<ClipLink>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LinkingStoresTheRelationshipInTheDirectionItWasCreated()
    {
        _links.Setup(x => x.FindAsync(1, 2, ClipLinkType.Sequel, It.IsAny<CancellationToken>()))
              .ReturnsAsync((ClipLink?)null);

        ClipLink? stored = null;
        _links.Setup(x => x.AddAsync(It.IsAny<ClipLink>(), It.IsAny<CancellationToken>()))
              .Callback<ClipLink, CancellationToken>((l, _) => { l.Id = 7; stored = l; })
              .Returns(Task.CompletedTask);

        var id = await _service.LinkAsync(1, 2, ClipLinkType.Sequel, "next round");

        Assert.Equal(7, id);
        Assert.Equal(1, stored!.SourceClipId);
        Assert.Equal(2, stored.TargetClipId);
        Assert.Equal(ClipLinkType.Sequel, stored.LinkType);
        Assert.Equal("next round", stored.Note);
    }

    [Fact]
    public async Task LinkingTwiceReturnsTheExistingLinkRatherThanFailing()
    {
        // The user asked for a relationship that already exists, and they got it.
        _links.Setup(x => x.FindAsync(1, 2, ClipLinkType.SameMoment, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ClipLink { Id = 5 });

        var id = await _service.LinkAsync(1, 2, ClipLinkType.SameMoment);

        Assert.Equal(5, id);
        _links.Verify(x => x.AddAsync(It.IsAny<ClipLink>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RelinkingTheOtherWayRoundDoesNotCreateAContradictorySecondRow()
    {
        // Linking B to A as a Sequel after A to B would otherwise assert each follows the other.
        _links.Setup(x => x.FindAsync(2, 1, ClipLinkType.Sequel, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ClipLink { Id = 5, SourceClipId = 1, TargetClipId = 2 });

        var id = await _service.LinkAsync(2, 1, ClipLinkType.Sequel);

        Assert.Equal(5, id);
        _links.Verify(x => x.AddAsync(It.IsAny<ClipLink>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ABlankNoteIsStoredAsNothingRatherThanWhitespace()
    {
        _links.Setup(x => x.FindAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<ClipLinkType>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((ClipLink?)null);

        ClipLink? stored = null;
        _links.Setup(x => x.AddAsync(It.IsAny<ClipLink>(), It.IsAny<CancellationToken>()))
              .Callback<ClipLink, CancellationToken>((l, _) => stored = l)
              .Returns(Task.CompletedTask);

        await _service.LinkAsync(1, 2, ClipLinkType.Variant, "   ");

        Assert.Null(stored!.Note);
    }

    [Fact]
    public async Task UnlinkingRemovesTheLink()
    {
        await _service.UnlinkAsync(9);

        _links.Verify(x => x.DeleteAsync(9, It.IsAny<CancellationToken>()), Times.Once);
    }
}
