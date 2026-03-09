using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Data.Repositories;
using Xunit;

namespace ClipStudio.Tests.Repositories;

/// <summary>
/// Integration tests for <see cref="ClipRepository"/> using an in-memory SQLite database.
/// </summary>
public sealed class ClipRepositoryTests : IDisposable
{
    private readonly Data.AppDbContext _context;
    private readonly ClipRepository _repository;

    /// <summary>Initializes a fresh database and repository instance for each test.</summary>
    public ClipRepositoryTests()
    {
        _context = TestDbContextFactory.Create();
        _repository = new ClipRepository(_context);
    }

    /// <inheritdoc/>
    public void Dispose() => _context.Dispose();

    private static SourceFolder CreateSourceFolder() => new()
    {
        Path = "/test/clips",
        IsActive = true
    };

    private static Clip CreateClip(SourceFolder folder, string fileName = "Replay 2025-01-01.mp4") => new()
    {
        SourceFolder = folder,
        FilePath = $"/test/clips/{fileName}",
        FileName = fileName,
        Duration = TimeSpan.FromMinutes(2),
        Resolution = "1920x1080",
        FileSizeBytes = 104857600,
        CreatedAt = DateTime.UtcNow,
        ImportedAt = DateTime.UtcNow,
        Status = ClipStatus.Unreviewed
    };

    [Fact]
    public async Task AddAsync_Then_GetByIdAsync_ReturnsClip()
    {
        var folder = CreateSourceFolder();
        _context.SourceFolders.Add(folder);
        await _context.SaveChangesAsync();

        var clip = CreateClip(folder);
        await _repository.AddAsync(clip);

        var retrieved = await _repository.GetByIdAsync(clip.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(clip.FileName, retrieved.FileName);
        Assert.Equal(ClipStatus.Unreviewed, retrieved.Status);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistentId_ReturnsNull()
    {
        var result = await _repository.GetByIdAsync(9999);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByStatusAsync_ReturnsOnlyMatchingStatus()
    {
        var folder = CreateSourceFolder();
        _context.SourceFolders.Add(folder);
        await _context.SaveChangesAsync();

        var unreviewed = CreateClip(folder, "Clip1.mp4");
        var reviewed = CreateClip(folder, "Clip2.mp4");
        reviewed.Status = ClipStatus.Reviewed;

        await _repository.AddAsync(unreviewed);
        await _repository.AddAsync(reviewed);

        var results = await _repository.GetByStatusAsync(ClipStatus.Unreviewed);

        Assert.Single(results);
        Assert.Equal("Clip1.mp4", results[0].FileName);
    }

    [Fact]
    public async Task ExistsByFilePathAsync_ExistingPath_ReturnsTrue()
    {
        var folder = CreateSourceFolder();
        _context.SourceFolders.Add(folder);
        await _context.SaveChangesAsync();

        var clip = CreateClip(folder);
        await _repository.AddAsync(clip);

        var exists = await _repository.ExistsByFilePathAsync(clip.FilePath);

        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsByFilePathAsync_UnknownPath_ReturnsFalse()
    {
        var exists = await _repository.ExistsByFilePathAsync("/does/not/exist.mp4");
        Assert.False(exists);
    }

    [Fact]
    public async Task UpdateAsync_PersistsChanges()
    {
        var folder = CreateSourceFolder();
        _context.SourceFolders.Add(folder);
        await _context.SaveChangesAsync();

        var clip = CreateClip(folder);
        await _repository.AddAsync(clip);

        clip.Status = ClipStatus.Reviewed;
        clip.Rating = 4;
        await _repository.UpdateAsync(clip);

        var updated = await _repository.GetByIdAsync(clip.Id);

        Assert.Equal(ClipStatus.Reviewed, updated!.Status);
        Assert.Equal(4, updated.Rating);
    }

    [Fact]
    public async Task DeleteAsync_RemovesClip()
    {
        var folder = CreateSourceFolder();
        _context.SourceFolders.Add(folder);
        await _context.SaveChangesAsync();

        var clip = CreateClip(folder);
        await _repository.AddAsync(clip);
        var id = clip.Id;

        await _repository.DeleteAsync(id);

        var result = await _repository.GetByIdAsync(id);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByTagsAsync_ClipLevelTag_ReturnsClip()
    {
        var folder = CreateSourceFolder();
        _context.SourceFolders.Add(folder);
        var tag = new Tag { Name = "Funny", Type = TagType.General };
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync();

        var clip = CreateClip(folder);
        _context.Clips.Add(clip);
        await _context.SaveChangesAsync();

        _context.ClipTags.Add(new ClipTag { ClipId = clip.Id, TagId = tag.Id });
        await _context.SaveChangesAsync();

        var results = await _repository.GetByTagsAsync([tag.Id]);

        Assert.Single(results);
        Assert.Equal(clip.Id, results[0].Id);
    }

    [Fact]
    public async Task GetByTagsAsync_HighlightLevelTag_ReturnsParentClip()
    {
        var folder = CreateSourceFolder();
        _context.SourceFolders.Add(folder);
        var tag = new Tag { Name = "Clutch", Type = TagType.General };
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync();

        var clip = CreateClip(folder);
        _context.Clips.Add(clip);
        await _context.SaveChangesAsync();

        var highlight = new Highlight
        {
            ClipId = clip.Id,
            StartTime = TimeSpan.FromSeconds(10),
            EndTime = TimeSpan.FromSeconds(25),
            CreatedAt = DateTime.UtcNow
        };
        _context.Highlights.Add(highlight);
        await _context.SaveChangesAsync();

        _context.HighlightTags.Add(new HighlightTag { HighlightId = highlight.Id, TagId = tag.Id });
        await _context.SaveChangesAsync();

        var results = await _repository.GetByTagsAsync([tag.Id]);

        Assert.Single(results);
        Assert.Equal(clip.Id, results[0].Id);
    }

    [Fact]
    public async Task GetByTagsAsync_UnrelatedTag_ReturnsEmpty()
    {
        var folder = CreateSourceFolder();
        _context.SourceFolders.Add(folder);
        await _context.SaveChangesAsync();

        var clip = CreateClip(folder);
        await _repository.AddAsync(clip);

        var results = await _repository.GetByTagsAsync([999]);

        Assert.Empty(results);
    }
}
