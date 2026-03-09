using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Data.Repositories;
using Xunit;

namespace ClipStudio.Tests.Repositories;

/// <summary>
/// Integration tests for <see cref="TagRepository"/> using an in-memory SQLite database.
/// </summary>
public sealed class TagRepositoryTests : IDisposable
{
    private readonly Data.AppDbContext _context;
    private readonly TagRepository _repository;

    /// <summary>Initializes a fresh database and repository instance for each test.</summary>
    public TagRepositoryTests()
    {
        _context = TestDbContextFactory.Create();
        _repository = new TagRepository(_context);
    }

    /// <inheritdoc/>
    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task AddAsync_Then_GetByIdAsync_ReturnsTag()
    {
        var tag = new Tag { Name = "Funny", Color = "#FF0000", Type = TagType.General };
        await _repository.AddAsync(tag);

        var retrieved = await _repository.GetByIdAsync(tag.Id);

        Assert.NotNull(retrieved);
        Assert.Equal("Funny", retrieved.Name);
        Assert.Equal(TagType.General, retrieved.Type);
    }

    [Fact]
    public async Task GetRootsAsync_ReturnsOnlyTagsWithNoParent()
    {
        var parent = new Tag { Name = "Kills", Type = TagType.General };
        await _repository.AddAsync(parent);

        var child = new Tag { Name = "Headshot", Type = TagType.General, ParentTagId = parent.Id };
        await _repository.AddAsync(child);

        var roots = await _repository.GetRootsAsync();

        Assert.Single(roots);
        Assert.Equal("Kills", roots[0].Name);
    }

    [Fact]
    public async Task GetDescendantIdsAsync_ReturnsAllDescendants()
    {
        // Tree: Kills -> Headshot -> (nothing), Kills -> Longshot
        var kills = new Tag { Name = "Kills", Type = TagType.General };
        await _repository.AddAsync(kills);

        var headshot = new Tag { Name = "Headshot", Type = TagType.General, ParentTagId = kills.Id };
        var longshot = new Tag { Name = "Longshot", Type = TagType.General, ParentTagId = kills.Id };
        await _repository.AddAsync(headshot);
        await _repository.AddAsync(longshot);

        var combo = new Tag { Name = "KillerCombo", Type = TagType.General, ParentTagId = headshot.Id };
        await _repository.AddAsync(combo);

        var descendants = await _repository.GetDescendantIdsAsync(kills.Id);

        Assert.Equal(3, descendants.Count);
        Assert.Contains(headshot.Id, descendants);
        Assert.Contains(longshot.Id, descendants);
        Assert.Contains(combo.Id, descendants);
    }

    [Fact]
    public async Task GetDescendantIdsAsync_LeafTag_ReturnsEmpty()
    {
        var tag = new Tag { Name = "Funny", Type = TagType.General };
        await _repository.AddAsync(tag);

        var descendants = await _repository.GetDescendantIdsAsync(tag.Id);

        Assert.Empty(descendants);
    }

    [Fact]
    public async Task AddRelationAsync_StoresBothDirections()
    {
        var gameplay = new Tag { Name = "Gameplay", Type = TagType.General };
        var pubpush = new Tag { Name = "PubPush", Type = TagType.General };
        await _repository.AddAsync(gameplay);
        await _repository.AddAsync(pubpush);

        await _repository.AddRelationAsync(gameplay.Id, pubpush.Id);

        var forward = await _context.TagRelations
            .FindAsync(gameplay.Id, pubpush.Id);
        var reverse = await _context.TagRelations
            .FindAsync(pubpush.Id, gameplay.Id);

        Assert.NotNull(forward);
        Assert.NotNull(reverse);
    }

    [Fact]
    public async Task AddRelationAsync_CalledTwice_DoesNotDuplicate()
    {
        var a = new Tag { Name = "A", Type = TagType.General };
        var b = new Tag { Name = "B", Type = TagType.General };
        await _repository.AddAsync(a);
        await _repository.AddAsync(b);

        await _repository.AddRelationAsync(a.Id, b.Id);
        await _repository.AddRelationAsync(a.Id, b.Id);

        var count = _context.TagRelations.Count(tr =>
            (tr.TagId == a.Id && tr.RelatedTagId == b.Id) ||
            (tr.TagId == b.Id && tr.RelatedTagId == a.Id));

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task RemoveRelationAsync_DeletesBothDirections()
    {
        var a = new Tag { Name = "A", Type = TagType.General };
        var b = new Tag { Name = "B", Type = TagType.General };
        await _repository.AddAsync(a);
        await _repository.AddAsync(b);

        await _repository.AddRelationAsync(a.Id, b.Id);
        await _repository.RemoveRelationAsync(a.Id, b.Id);

        var count = _context.TagRelations.Count(tr =>
            (tr.TagId == a.Id && tr.RelatedTagId == b.Id) ||
            (tr.TagId == b.Id && tr.RelatedTagId == a.Id));

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTag()
    {
        var tag = new Tag { Name = "ToDelete", Type = TagType.General };
        await _repository.AddAsync(tag);
        var id = tag.Id;

        await _repository.DeleteAsync(id);

        var result = await _repository.GetByIdAsync(id);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByTypeAsync_ReturnsOnlyMatchingType()
    {
        var gameTag = new Tag { Name = "Apex Legends", Type = TagType.Game };
        var generalTag = new Tag { Name = "Funny", Type = TagType.General };
        await _repository.AddAsync(gameTag);
        await _repository.AddAsync(generalTag);

        var games = await _repository.GetByTypeAsync(TagType.Game);

        Assert.Single(games);
        Assert.Equal("Apex Legends", games[0].Name);
    }
}
