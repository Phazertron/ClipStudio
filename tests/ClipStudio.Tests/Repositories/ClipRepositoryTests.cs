using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Models;
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

    // ---- SearchAsync: filters pushed down into the EF query ----

    /// <summary>
    /// Seeds a source folder plus <paramref name="count"/> clips and returns them in insertion order.
    /// </summary>
    /// <param name="count">The number of clips to create.</param>
    /// <returns>The persisted clips.</returns>
    private async Task<List<Clip>> SeedClipsAsync(int count)
    {
        var folder = CreateSourceFolder();
        _context.SourceFolders.Add(folder);
        await _context.SaveChangesAsync();

        var clips = new List<Clip>();
        for (var i = 0; i < count; i++)
        {
            var clip = CreateClip(folder, $"Replay {i}.mp4");
            await _repository.AddAsync(clip);
            clips.Add(clip);
        }

        return clips;
    }

    [Fact]
    public async Task SearchAsync_StatusFilter_ReturnsOnlyMatchingStatus()
    {
        var clips = await SeedClipsAsync(3);
        clips[1].Status = ClipStatus.Reviewed;
        await _repository.UpdateAsync(clips[1]);

        var results = await _repository.SearchAsync(new ClipSearchQuery { Status = ClipStatus.Unreviewed });

        Assert.Equal(2, results.Count);
        Assert.All(results, c => Assert.Equal(ClipStatus.Unreviewed, c.Status));
    }

    [Fact]
    public async Task SearchAsync_ExcludeArchived_DropsArchivedClips_OnlyWhenStatusIsUnset()
    {
        var clips = await SeedClipsAsync(2);
        clips[0].Status = ClipStatus.Archived;
        await _repository.UpdateAsync(clips[0]);

        var excluded = await _repository.SearchAsync(new ClipSearchQuery { ExcludeArchived = true });
        Assert.Single(excluded);
        Assert.Equal(clips[1].Id, excluded[0].Id);

        // An explicit status wins over ExcludeArchived.
        var explicitArchived = await _repository.SearchAsync(
            new ClipSearchQuery { ExcludeArchived = true, Status = ClipStatus.Archived });
        Assert.Single(explicitArchived);
        Assert.Equal(clips[0].Id, explicitArchived[0].Id);
    }

    [Fact]
    public async Task SearchAsync_MinRating_ExcludesLowerRatedClips()
    {
        var clips = await SeedClipsAsync(2);
        clips[0].Rating = 1;
        clips[1].Rating = 4;
        await _repository.UpdateAsync(clips[0]);
        await _repository.UpdateAsync(clips[1]);

        var results = await _repository.SearchAsync(new ClipSearchQuery { MinRating = 3 });

        Assert.Single(results);
        Assert.Equal(4, results[0].Rating);
    }

    [Fact]
    public async Task SearchAsync_FavouriteAndDurationFilters_AreApplied()
    {
        var clips = await SeedClipsAsync(2);
        clips[0].IsFavourite = true;
        clips[0].Duration = TimeSpan.FromMinutes(10);
        await _repository.UpdateAsync(clips[0]);

        var favourites = await _repository.SearchAsync(new ClipSearchQuery { IsFavourite = true });
        Assert.Single(favourites);
        Assert.Equal(clips[0].Id, favourites[0].Id);

        var longClips = await _repository.SearchAsync(
            new ClipSearchQuery { MinDuration = TimeSpan.FromMinutes(5) });
        Assert.Single(longClips);
        Assert.Equal(clips[0].Id, longClips[0].Id);

        var shortClips = await _repository.SearchAsync(
            new ClipSearchQuery { MaxDuration = TimeSpan.FromMinutes(5) });
        Assert.Single(shortClips);
        Assert.Equal(clips[1].Id, shortClips[0].Id);
    }

    [Fact]
    public async Task SearchAsync_HasHighlights_SplitsClipsBothWays()
    {
        var clips = await SeedClipsAsync(2);
        _context.Highlights.Add(new Highlight
        {
            ClipId    = clips[0].Id,
            Label     = "Ace",
            StartTime = TimeSpan.Zero,
            EndTime   = TimeSpan.FromSeconds(10)
        });
        await _context.SaveChangesAsync();

        var withHighlights = await _repository.SearchAsync(new ClipSearchQuery { HasHighlights = true });
        Assert.Single(withHighlights);
        Assert.Equal(clips[0].Id, withHighlights[0].Id);

        var withoutHighlights = await _repository.SearchAsync(new ClipSearchQuery { HasHighlights = false });
        Assert.Single(withoutHighlights);
        Assert.Equal(clips[1].Id, withoutHighlights[0].Id);
    }

    [Fact]
    public async Task SearchAsync_TagFilter_MatchesClipTagsAndHighlightTags()
    {
        var clips = await SeedClipsAsync(3);

        var tag = new Tag { Name = "Clutch", Type = TagType.General };
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync();

        _context.ClipTags.Add(new ClipTag { ClipId = clips[0].Id, TagId = tag.Id });

        var highlight = new Highlight
        {
            ClipId    = clips[1].Id,
            Label     = "Clutch moment",
            StartTime = TimeSpan.Zero,
            EndTime   = TimeSpan.FromSeconds(5)
        };
        _context.Highlights.Add(highlight);
        await _context.SaveChangesAsync();

        _context.HighlightTags.Add(new HighlightTag { HighlightId = highlight.Id, TagId = tag.Id });
        await _context.SaveChangesAsync();

        var results = await _repository.SearchAsync(new ClipSearchQuery { TagIds = [tag.Id] });

        Assert.Equal([clips[0].Id, clips[1].Id], results.Select(c => c.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task SearchAsync_ExcludedTagIds_HidesClipsTaggedDirectlyOrViaHighlights()
    {
        var clips = await SeedClipsAsync(3);

        var tag = new Tag { Name = "Boring", Type = TagType.General };
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync();

        _context.ClipTags.Add(new ClipTag { ClipId = clips[0].Id, TagId = tag.Id });

        var highlight = new Highlight
        {
            ClipId    = clips[1].Id,
            Label     = "Nothing happens",
            StartTime = TimeSpan.Zero,
            EndTime   = TimeSpan.FromSeconds(5)
        };
        _context.Highlights.Add(highlight);
        await _context.SaveChangesAsync();

        _context.HighlightTags.Add(new HighlightTag { HighlightId = highlight.Id, TagId = tag.Id });
        await _context.SaveChangesAsync();

        var results = await _repository.SearchAsync(new ClipSearchQuery { ExcludedTagIds = [tag.Id] });

        Assert.Single(results);
        Assert.Equal(clips[2].Id, results[0].Id);
    }

    [Fact]
    public async Task SearchAsync_PlayerFilters_IncludeAndExclude()
    {
        var clips = await SeedClipsAsync(2);

        var player = new Player { DisplayName = "Phazertron" };
        _context.Players.Add(player);
        await _context.SaveChangesAsync();

        _context.ClipPlayers.Add(new ClipPlayer { ClipId = clips[0].Id, PlayerId = player.Id });
        await _context.SaveChangesAsync();

        var included = await _repository.SearchAsync(new ClipSearchQuery { PlayerIds = [player.Id] });
        Assert.Single(included);
        Assert.Equal(clips[0].Id, included[0].Id);

        var excluded = await _repository.SearchAsync(new ClipSearchQuery { ExcludedPlayerIds = [player.Id] });
        Assert.Single(excluded);
        Assert.Equal(clips[1].Id, excluded[0].Id);
    }

    [Fact]
    public async Task SearchAsync_CreatedDateRange_IsApplied()
    {
        var clips = await SeedClipsAsync(2);
        clips[0].CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        clips[1].CreatedAt = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await _repository.UpdateAsync(clips[0]);
        await _repository.UpdateAsync(clips[1]);

        var results = await _repository.SearchAsync(new ClipSearchQuery
        {
            CreatedFrom = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        Assert.Single(results);
        Assert.Equal(clips[1].Id, results[0].Id);
    }

    [Fact]
    public async Task SearchAsync_ExcludesSoftDeletedClips()
    {
        var clips = await SeedClipsAsync(2);
        clips[0].IsDeleted = true;
        await _repository.UpdateAsync(clips[0]);

        var results = await _repository.SearchAsync(new ClipSearchQuery());

        Assert.Single(results);
        Assert.Equal(clips[1].Id, results[0].Id);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsAllClips_NewestFirst()
    {
        var clips = await SeedClipsAsync(3);
        clips[0].CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        clips[1].CreatedAt = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        clips[2].CreatedAt = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var clip in clips)
            await _repository.UpdateAsync(clip);

        var results = await _repository.SearchAsync(new ClipSearchQuery());

        Assert.Equal([clips[1].Id, clips[2].Id, clips[0].Id], results.Select(c => c.Id));
    }
}
