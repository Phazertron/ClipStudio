using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;

namespace ClipStudio.Application.Services;

/// <summary>
/// Computes aggregated statistics for the clip library by querying the clip, tag, and player repositories.
/// </summary>
public sealed class StatsService : IStatsService
{
    private readonly IClipRepository _clips;
    private readonly IHighlightRepository _highlights;

    /// <summary>Initializes a new instance of <see cref="StatsService"/>.</summary>
    public StatsService(IClipRepository clips, IHighlightRepository highlights)
    {
        _clips      = clips;
        _highlights = highlights;
    }

    /// <inheritdoc/>
    public async Task<LibraryStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var allClips = await _clips.GetAllAsync(cancellationToken);
        var allHighlights = await _highlights.GetAllAsync(cancellationToken);

        var stats = new LibraryStatistics
        {
            TotalClips       = allClips.Count,
            UnreviewedClips  = allClips.Count(c => c.Status == ClipStatus.Unreviewed),
            ReviewedClips    = allClips.Count(c => c.Status == ClipStatus.Reviewed),
            ArchivedClips    = allClips.Count(c => c.Status == ClipStatus.Archived),
            TotalHighlights  = allHighlights.Count,
            TotalDuration    = TimeSpan.FromTicks(allClips.Sum(c => c.Duration.Ticks)),
            TotalPlayCount   = allClips.Sum(c => c.PlayCount),
            FavouriteClips   = allClips.Count(c => c.IsFavourite),
            RatedClips       = allClips.Count(c => c.Rating > 0),
        };

        // Rating distribution: index 1-5.
        for (var star = 1; star <= 5; star++)
            stats.RatingDistribution[star] = allClips.Count(c => c.Rating == star);

        // Top games (Tags of type Game attached to clips, counted by clip).
        stats.TopGames = allClips
            .SelectMany(c => c.ClipTags.Select(ct => ct.Tag))
            .Where(t => t?.Type == ClipStudio.Core.Enums.TagType.Game)
            .GroupBy(t => t!.Name)
            .Select(g => (Name: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToList();

        // Top general tags (Tags of type General attached to clips, counted by clip).
        stats.TopTags = allClips
            .SelectMany(c => c.ClipTags.Select(ct => ct.Tag))
            .Where(t => t?.Type == ClipStudio.Core.Enums.TagType.General)
            .GroupBy(t => t!.Name)
            .Select(g => (Name: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToList();

        // Top players by clip count.
        stats.TopPlayers = allClips
            .SelectMany(c => c.ClipPlayers.Select(cp => cp.Player))
            .Where(p => p is not null)
            .GroupBy(p => p!.DisplayName)
            .Select(g => (Name: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToList();

        // Most-played clips (only those with at least one play).
        stats.MostPlayed = allClips
            .Where(c => c.PlayCount > 0)
            .OrderByDescending(c => c.PlayCount)
            .Take(10)
            .Select(c => (FileName: c.FileName, PlayCount: c.PlayCount))
            .ToList();

        return stats;
    }
}
