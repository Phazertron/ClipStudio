namespace ClipStudio.Application.Models;

/// <summary>
/// Aggregated statistics computed from the clip library.
/// Returned by <see cref="Interfaces.IStatsService.GetStatisticsAsync"/>.
/// </summary>
public sealed class LibraryStatistics
{
    /// <summary>Gets or sets the total number of active (non-trashed) clips in the library.</summary>
    public int TotalClips { get; set; }

    /// <summary>Gets or sets the number of clips in Unreviewed status.</summary>
    public int UnreviewedClips { get; set; }

    /// <summary>Gets or sets the number of clips in Reviewed status.</summary>
    public int ReviewedClips { get; set; }

    /// <summary>Gets or sets the number of clips in Archived status.</summary>
    public int ArchivedClips { get; set; }

    /// <summary>Gets or sets the total number of highlights across all clips.</summary>
    public int TotalHighlights { get; set; }

    /// <summary>Gets or sets the combined duration of all non-trashed clips.</summary>
    public TimeSpan TotalDuration { get; set; }

    /// <summary>Gets or sets the total number of times clips have been opened (sum of PlayCount).</summary>
    public int TotalPlayCount { get; set; }

    /// <summary>Gets or sets the number of favourite clips.</summary>
    public int FavouriteClips { get; set; }

    /// <summary>Gets or sets the number of rated clips (rating &gt; 0).</summary>
    public int RatedClips { get; set; }

    /// <summary>
    /// Gets or sets the per-rating-star clip counts, indexed 1 to 5.
    /// <c>RatingDistribution[1]</c> = clips rated exactly 1 star, etc.
    /// Index 0 is unused.
    /// </summary>
    public int[] RatingDistribution { get; set; } = new int[6];

    /// <summary>Gets or sets the top game tags by clip count, ordered descending. Each tuple is (GameName, Count).</summary>
    public IReadOnlyList<(string Name, int Count)> TopGames { get; set; } = Array.Empty<(string, int)>();

    /// <summary>Gets or sets the top general tags by clip count, ordered descending. Each tuple is (TagName, Count).</summary>
    public IReadOnlyList<(string Name, int Count)> TopTags { get; set; } = Array.Empty<(string, int)>();

    /// <summary>Gets or sets the top players by clip count, ordered descending. Each tuple is (PlayerName, Count).</summary>
    public IReadOnlyList<(string Name, int Count)> TopPlayers { get; set; } = Array.Empty<(string, int)>();

    /// <summary>Gets or sets the most-played clips, ordered by PlayCount descending. Each tuple is (FileName, PlayCount).</summary>
    public IReadOnlyList<(string FileName, int PlayCount)> MostPlayed { get; set; } = Array.Empty<(string, int)>();
}
