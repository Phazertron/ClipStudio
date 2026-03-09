using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Data.Repositories;
using Xunit;

namespace ClipStudio.Tests.Repositories;

/// <summary>
/// Integration tests for <see cref="HighlightRepository"/> using an in-memory SQLite database.
/// </summary>
public sealed class HighlightRepositoryTests : IDisposable
{
    private readonly Data.AppDbContext _context;
    private readonly HighlightRepository _repository;

    /// <summary>Initializes a fresh database and repository instance for each test.</summary>
    public HighlightRepositoryTests()
    {
        _context = TestDbContextFactory.Create();
        _repository = new HighlightRepository(_context);
    }

    /// <inheritdoc/>
    public void Dispose() => _context.Dispose();

    private async Task<Clip> CreatePersistedClipAsync()
    {
        var folder = new SourceFolder { Path = "/clips", IsActive = true };
        _context.SourceFolders.Add(folder);
        var clip = new Clip
        {
            SourceFolder = folder,
            FilePath = "/clips/test.mp4",
            FileName = "test.mp4",
            Duration = TimeSpan.FromMinutes(5),
            Resolution = "1920x1080",
            FileSizeBytes = 1024,
            CreatedAt = DateTime.UtcNow,
            ImportedAt = DateTime.UtcNow,
            Status = ClipStatus.Unreviewed
        };
        _context.Clips.Add(clip);
        await _context.SaveChangesAsync();
        return clip;
    }

    [Fact]
    public async Task AddAsync_Then_GetByIdAsync_ReturnsHighlight()
    {
        var clip = await CreatePersistedClipAsync();

        var highlight = new Highlight
        {
            ClipId = clip.Id,
            Label = "Clutch moment",
            StartTime = TimeSpan.FromSeconds(30),
            EndTime = TimeSpan.FromSeconds(45),
            CreatedAt = DateTime.UtcNow
        };
        await _repository.AddAsync(highlight);

        var retrieved = await _repository.GetByIdAsync(highlight.Id);

        Assert.NotNull(retrieved);
        Assert.Equal("Clutch moment", retrieved.Label);
        Assert.Equal(TimeSpan.FromSeconds(30), retrieved.StartTime);
        Assert.Equal(TimeSpan.FromSeconds(45), retrieved.EndTime);
    }

    [Fact]
    public async Task Duration_ComputedCorrectly()
    {
        var highlight = new Highlight
        {
            StartTime = TimeSpan.FromSeconds(10),
            EndTime = TimeSpan.FromSeconds(25)
        };

        Assert.Equal(TimeSpan.FromSeconds(15), highlight.Duration);
    }

    [Fact]
    public async Task GetByClipAsync_ReturnsHighlightsOrderedByStartTime()
    {
        var clip = await CreatePersistedClipAsync();

        var h1 = new Highlight { ClipId = clip.Id, StartTime = TimeSpan.FromSeconds(60), EndTime = TimeSpan.FromSeconds(75), CreatedAt = DateTime.UtcNow };
        var h2 = new Highlight { ClipId = clip.Id, StartTime = TimeSpan.FromSeconds(10), EndTime = TimeSpan.FromSeconds(20), CreatedAt = DateTime.UtcNow };
        await _repository.AddAsync(h1);
        await _repository.AddAsync(h2);

        var results = await _repository.GetByClipAsync(clip.Id);

        Assert.Equal(2, results.Count);
        Assert.Equal(h2.Id, results[0].Id);
        Assert.Equal(h1.Id, results[1].Id);
    }

    [Fact]
    public async Task OverlappingHighlights_AreStoredIndependently()
    {
        var clip = await CreatePersistedClipAsync();

        // h1 and h2 share a time range — this must be allowed by design.
        var h1 = new Highlight { ClipId = clip.Id, StartTime = TimeSpan.FromSeconds(0), EndTime = TimeSpan.FromSeconds(60), CreatedAt = DateTime.UtcNow };
        var h2 = new Highlight { ClipId = clip.Id, StartTime = TimeSpan.FromSeconds(30), EndTime = TimeSpan.FromSeconds(45), CreatedAt = DateTime.UtcNow };
        await _repository.AddAsync(h1);
        await _repository.AddAsync(h2);

        var results = await _repository.GetByClipAsync(clip.Id);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task DeleteAsync_RemovesHighlight()
    {
        var clip = await CreatePersistedClipAsync();

        var highlight = new Highlight
        {
            ClipId = clip.Id,
            StartTime = TimeSpan.FromSeconds(5),
            EndTime = TimeSpan.FromSeconds(15),
            CreatedAt = DateTime.UtcNow
        };
        await _repository.AddAsync(highlight);
        var id = highlight.Id;

        await _repository.DeleteAsync(id);

        var result = await _repository.GetByIdAsync(id);
        Assert.Null(result);
    }
}
