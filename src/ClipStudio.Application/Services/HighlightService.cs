using System;
using System.IO;
using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ClipStudio.Application.Services;

/// <summary>
/// Application service for creating, editing, and tagging highlight time ranges within clips.
/// </summary>
public sealed class HighlightService : IHighlightService
{
    private readonly IHighlightRepository _highlights;
    private readonly IClipRepository _clips;
    private readonly IMediaService _media;
    private readonly ILogger<HighlightService> _logger;

    /// <summary>Initializes a new instance of <see cref="HighlightService"/>.</summary>
    public HighlightService(
        IHighlightRepository highlights,
        IClipRepository clips,
        IMediaService media,
        ILogger<HighlightService> logger)
    {
        _highlights = highlights;
        _clips      = clips;
        _media      = media;
        _logger     = logger;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Highlight>> GetAllAsync(CancellationToken cancellationToken = default)
        => _highlights.GetAllAsync(cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<Highlight>> GetByClipAsync(int clipId, CancellationToken cancellationToken = default)
        => _highlights.GetByClipAsync(clipId, cancellationToken);

    /// <inheritdoc/>
    public Task<Highlight?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => _highlights.GetByIdAsync(id, cancellationToken);

    /// <inheritdoc/>
    public async Task<Highlight> CreateAsync(
        int clipId,
        TimeSpan startTime,
        TimeSpan endTime,
        string? label = null,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        if (endTime <= startTime)
            throw new ArgumentException("EndTime must be greater than StartTime.");

        var highlight = new Highlight
        {
            ClipId = clipId,
            StartTime = startTime,
            EndTime = endTime,
            Label = label,
            Notes = notes,
            CreatedAt = DateTime.UtcNow
        };

        await _highlights.AddAsync(highlight, cancellationToken);
        _logger.LogInformation(
            "Created highlight {Id} on clip {ClipId} [{Start} - {End}].",
            highlight.Id, clipId, startTime, endTime);

        // Generate a thumbnail at the midpoint of the highlight.
        // Done sequentially (not fire-and-forget) to avoid concurrent DbContext access, which
        // would cause an EF Core concurrency exception when the caller refreshes the highlight list
        // immediately after CreateAsync returns.
        try
        {
            var clip = await _clips.GetByIdAsync(clipId, cancellationToken);
            if (clip is not null && File.Exists(clip.FilePath))
            {
                var midpoint      = startTime + TimeSpan.FromSeconds((endTime - startTime).TotalSeconds / 2.0);
                var outputDir     = Path.GetDirectoryName(clip.ThumbnailPath) ?? Path.GetTempPath();
                // Use a unique filename suffix (highlight ID) to avoid overwriting the clip thumbnail.
                var thumbnailPath = await _media.GenerateThumbnailAsync(
                    clip.FilePath, outputDir, midpoint, $"hl{highlight.Id}", cancellationToken);

                highlight.ThumbnailPath = thumbnailPath;
                await _highlights.UpdateAsync(highlight, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate thumbnail for highlight {HighlightId}.", highlight.Id);
        }

        return highlight;
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(
        int highlightId,
        TimeSpan startTime,
        TimeSpan endTime,
        string? label,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        if (endTime <= startTime)
            throw new ArgumentException("EndTime must be greater than StartTime.");

        var highlight = await RequireHighlightAsync(highlightId, cancellationToken);
        highlight.StartTime = startTime;
        highlight.EndTime = endTime;
        highlight.Label = label;
        highlight.Notes = notes;
        await _highlights.UpdateAsync(highlight, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task AddTagAsync(int highlightId, int tagId, CancellationToken cancellationToken = default)
    {
        var highlight = await RequireHighlightAsync(highlightId, cancellationToken);

        if (highlight.HighlightTags.Any(ht => ht.TagId == tagId))
            return;

        highlight.HighlightTags.Add(new HighlightTag { HighlightId = highlightId, TagId = tagId });
        await _highlights.UpdateAsync(highlight, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task RemoveTagAsync(int highlightId, int tagId, CancellationToken cancellationToken = default)
    {
        var highlight = await RequireHighlightAsync(highlightId, cancellationToken);
        var assignment = highlight.HighlightTags.FirstOrDefault(ht => ht.TagId == tagId);
        if (assignment is null)
            return;

        highlight.HighlightTags.Remove(assignment);
        await _highlights.UpdateAsync(highlight, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task SetRatingAsync(int highlightId, int rating, CancellationToken cancellationToken = default)
    {
        if (rating < 0 || rating > 5)
            throw new ArgumentOutOfRangeException(nameof(rating), "Rating must be between 0 and 5.");

        var highlight = await RequireHighlightAsync(highlightId, cancellationToken);
        highlight.Rating = rating;
        await _highlights.UpdateAsync(highlight, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task ToggleFavoriteAsync(int highlightId, CancellationToken cancellationToken = default)
    {
        var highlight = await RequireHighlightAsync(highlightId, cancellationToken);
        highlight.IsFavorite = !highlight.IsFavorite;
        await _highlights.UpdateAsync(highlight, cancellationToken);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(int highlightId, CancellationToken cancellationToken = default)
        => _highlights.DeleteAsync(highlightId, cancellationToken);

    private async Task<Highlight> RequireHighlightAsync(int highlightId, CancellationToken cancellationToken)
    {
        var highlight = await _highlights.GetByIdAsync(highlightId, cancellationToken);
        if (highlight is null)
            throw new InvalidOperationException($"Highlight with id {highlightId} was not found.");
        return highlight;
    }
}
