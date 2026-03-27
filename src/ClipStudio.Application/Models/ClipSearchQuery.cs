using ClipStudio.Core.Enums;

namespace ClipStudio.Application.Models;

/// <summary>
/// Encapsulates all filter and search parameters used to query the clip library.
/// All fields are optional; omitting a field means that criterion is not applied.
/// </summary>
public sealed class ClipSearchQuery
{
    /// <summary>
    /// Gets or sets tag identifiers to filter by.
    /// A clip matches if it (or any of its highlights) carries at least one of the listed tags.
    /// </summary>
    public IList<int> TagIds { get; set; } = [];

    /// <summary>
    /// Gets or sets whether descendant tags in the hierarchy are included when matching
    /// against <see cref="TagIds"/>. Defaults to true.
    /// </summary>
    public bool IncludeTagDescendants { get; set; } = true;

    /// <summary>Gets or sets the clip status to filter by. Null means all statuses are returned.</summary>
    public ClipStatus? Status { get; set; }

    /// <summary>
    /// When true, clips with <see cref="ClipStatus.Archived"/> status are excluded from results
    /// even when <see cref="Status"/> is null. Has no effect when <see cref="Status"/> is set explicitly.
    /// </summary>
    public bool ExcludeArchived { get; set; } = false;

    /// <summary>Gets or sets the earliest creation date (inclusive) to include.</summary>
    public DateTime? CreatedFrom { get; set; }

    /// <summary>Gets or sets the latest creation date (inclusive) to include.</summary>
    public DateTime? CreatedTo { get; set; }

    /// <summary>Gets or sets the minimum star rating required (0 = unrated, 1-5 = rated).</summary>
    public int MinRating { get; set; } = 0;

    /// <summary>Gets or sets whether to return only favourite clips. Null means all clips.</summary>
    public bool? IsFavourite { get; set; }

    /// <summary>Gets or sets whether to return only clips that have at least one highlight. Null means all clips.</summary>
    public bool? HasHighlights { get; set; }

    /// <summary>Gets or sets the minimum clip duration to include.</summary>
    public TimeSpan? MinDuration { get; set; }

    /// <summary>Gets or sets the maximum clip duration to include.</summary>
    public TimeSpan? MaxDuration { get; set; }

    /// <summary>
    /// Gets or sets free-text search applied against clip filename, highlight labels, and notes.
    /// Case-insensitive. Null or empty means no text filter is applied.
    /// </summary>
    public string? SearchText { get; set; }

    /// <summary>
    /// When <c>true</c> and <see cref="SearchText"/> is non-empty, the search is also applied
    /// against transcription segment text.  Clips that have a matching caption are included in
    /// results even if their filename or tags do not match.
    /// Defaults to <c>false</c> to avoid the extra DB query for normal searches.
    /// </summary>
    public bool SearchCaptions { get; set; } = false;

    /// <summary>
    /// Gets or sets player identifiers to filter by.
    /// A clip matches if it is tagged with at least one of the listed players.
    /// Empty list means no player filter is applied.
    /// </summary>
    public IList<int> PlayerIds { get; set; } = [];

    /// <summary>
    /// Gets or sets tag identifiers to exclude.
    /// A clip is hidden if it (or any of its highlights) carries any of the listed tags.
    /// Empty list means no tag exclusions are applied.
    /// </summary>
    public IList<int> ExcludedTagIds { get; set; } = [];

    /// <summary>
    /// Gets or sets player identifiers to exclude.
    /// A clip is hidden if it is tagged with any of the listed players.
    /// Empty list means no player exclusions are applied.
    /// </summary>
    public IList<int> ExcludedPlayerIds { get; set; } = [];
}
