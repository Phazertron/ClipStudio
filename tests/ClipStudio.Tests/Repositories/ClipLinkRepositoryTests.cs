using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Data.Repositories;
using Xunit;

namespace ClipStudio.Tests.Repositories;

/// <summary>
/// Integration tests for <see cref="ClipLinkRepository"/> using an in-memory SQLite database.
/// </summary>
/// <remarks>
/// A link is stored once, in the direction it was created, and read from both ends. Nearly every
/// test here is about that asymmetry holding.
/// </remarks>
public sealed class ClipLinkRepositoryTests : IDisposable
{
    private readonly Data.AppDbContext _context;
    private readonly ClipLinkRepository _repository;
    private readonly SourceFolder _folder;

    /// <summary>Initialises a fresh database and repository for each test.</summary>
    public ClipLinkRepositoryTests()
    {
        _context    = TestDbContextFactory.Create();
        _repository = new ClipLinkRepository(_context);

        _folder = new SourceFolder { Path = "/test/clips", IsActive = true };
        _context.SourceFolders.Add(_folder);
        _context.SaveChanges();
    }

    /// <inheritdoc/>
    public void Dispose() => _context.Dispose();

    private Clip AddClip(string fileName)
    {
        var clip = new Clip
        {
            SourceFolder = _folder,
            FilePath     = $"/test/clips/{fileName}",
            FileName     = fileName,
            Duration     = TimeSpan.FromMinutes(2),
            CreatedAt    = DateTime.UtcNow,
            ImportedAt   = DateTime.UtcNow,
        };

        _context.Clips.Add(clip);
        _context.SaveChanges();
        return clip;
    }

    private async Task<ClipLink> LinkAsync(Clip source, Clip target, ClipLinkType type = ClipLinkType.SameMoment)
    {
        var link = new ClipLink
        {
            SourceClipId = source.Id,
            TargetClipId = target.Id,
            LinkType     = type,
            CreatedAt    = DateTime.UtcNow,
        };

        await _repository.AddAsync(link);
        return link;
    }

    [Fact]
    public async Task AClipWithNoLinksReturnsNothing()
    {
        var clip = AddClip("alone.mp4");

        Assert.Empty(await _repository.GetForClipAsync(clip.Id));
    }

    [Fact]
    public async Task ALinkIsVisibleFromBothEnds()
    {
        // Stored once, read from both sides - the property the whole design rests on.
        var a = AddClip("a.mp4");
        var b = AddClip("b.mp4");
        await LinkAsync(a, b);

        Assert.Single(await _repository.GetForClipAsync(a.Id));
        Assert.Single(await _repository.GetForClipAsync(b.Id));
    }

    [Fact]
    public async Task BothClipsAreLoadedSoEitherEndCanBeShown()
    {
        var a = AddClip("a.mp4");
        var b = AddClip("b.mp4");
        await LinkAsync(a, b);

        var link = Assert.Single(await _repository.GetForClipAsync(b.Id));

        Assert.NotNull(link.SourceClip);
        Assert.NotNull(link.TargetClip);
        Assert.Equal("a.mp4", link.SourceClip.FileName);
        Assert.Equal("b.mp4", link.TargetClip.FileName);
    }

    [Fact]
    public async Task FindMatchesRegardlessOfWhichWayRoundItIsAsked()
    {
        // The unique index only covers the stored direction, so the duplicate check has to look
        // both ways or linking B to A after A to B would create a second row for one relationship.
        var a = AddClip("a.mp4");
        var b = AddClip("b.mp4");
        await LinkAsync(a, b, ClipLinkType.Sequel);

        Assert.NotNull(await _repository.FindAsync(a.Id, b.Id, ClipLinkType.Sequel));
        Assert.NotNull(await _repository.FindAsync(b.Id, a.Id, ClipLinkType.Sequel));
    }

    [Fact]
    public async Task FindDoesNotMatchADifferentKindOfRelationship()
    {
        // Two clips can be related in more than one way, so the type is part of the identity.
        var a = AddClip("a.mp4");
        var b = AddClip("b.mp4");
        await LinkAsync(a, b, ClipLinkType.SameMoment);

        Assert.Null(await _repository.FindAsync(a.Id, b.Id, ClipLinkType.Reaction));
    }

    [Fact]
    public async Task DeletingAClipTakesItsLinksWithIt()
    {
        // A link to a clip that no longer exists is not a relationship, it is a dangling row.
        var a = AddClip("a.mp4");
        var b = AddClip("b.mp4");
        var c = AddClip("c.mp4");
        await LinkAsync(a, b);
        await LinkAsync(c, a);

        _context.Clips.Remove(_context.Clips.Find(a.Id)!);
        await _context.SaveChangesAsync();

        Assert.Empty(await _repository.GetForClipAsync(b.Id));
        Assert.Empty(await _repository.GetForClipAsync(c.Id));
        Assert.Empty(_context.ClipLinks);
    }

    [Fact]
    public async Task ATrashedClipDropsOutOfTheListButTheLinkSurvives()
    {
        // Trashing is reversible, so the relationship has to come back with the clip.
        var a = AddClip("a.mp4");
        var b = AddClip("b.mp4");
        await LinkAsync(a, b);

        var trashed = _context.Clips.Find(b.Id)!;
        trashed.IsDeleted = true;
        await _context.SaveChangesAsync();

        Assert.Empty(await _repository.GetForClipAsync(a.Id));
        Assert.Single(_context.ClipLinks);

        trashed.IsDeleted = false;
        await _context.SaveChangesAsync();

        Assert.Single(await _repository.GetForClipAsync(a.Id));
    }

    [Fact]
    public async Task DeletingALinkLeavesBothClipsAlone()
    {
        var a = AddClip("a.mp4");
        var b = AddClip("b.mp4");
        var link = await LinkAsync(a, b);

        await _repository.DeleteAsync(link.Id);

        Assert.Empty(await _repository.GetForClipAsync(a.Id));
        Assert.Equal(2, _context.Clips.Count());
    }

    [Fact]
    public async Task DeletingALinkThatIsNotThereIsANoOp()
    {
        await _repository.DeleteAsync(9999);
        Assert.Empty(_context.ClipLinks);
    }

    [Fact]
    public async Task CountsLinksFromEitherEnd()
    {
        var a = AddClip("a.mp4");
        var b = AddClip("b.mp4");
        var c = AddClip("c.mp4");
        await LinkAsync(a, b);
        await LinkAsync(c, a);

        var counts = await _repository.CountByClipAsync([a.Id, b.Id, c.Id]);

        Assert.Equal(2, counts[a.Id]);
        Assert.Equal(1, counts[b.Id]);
        Assert.Equal(1, counts[c.Id]);
    }

    [Fact]
    public async Task CountingOmitsClipsWithNoLinks()
    {
        var a = AddClip("a.mp4");
        var lonely = AddClip("lonely.mp4");
        var b = AddClip("b.mp4");
        await LinkAsync(a, b);

        var counts = await _repository.CountByClipAsync([a.Id, lonely.Id, b.Id]);

        Assert.False(counts.ContainsKey(lonely.Id));
    }

    [Fact]
    public async Task CountingNothingAsksTheDatabaseNothing()
    {
        Assert.Empty(await _repository.CountByClipAsync([]));
    }
}
