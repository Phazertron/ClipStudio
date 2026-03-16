namespace ClipStudio.Core.Entities;

/// <summary>
/// Represents a saved set of library filter criteria that the user can recall by name.
/// All filter fields are stored as nullable values; null means the criterion is not active.
/// Tag and player lists are stored as comma-separated integer strings (e.g. "1,2,3").
/// A minus prefix ("-1,-2") indicates excluded IDs.
/// </summary>
public class FilterPreset
{
    /// <summary>Gets or sets the unique identifier of this preset.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the user-visible name for this preset.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the comma-separated list of included tag IDs (general + game tags combined).
    /// Null or empty means no tag inclusion filter is saved.
    /// </summary>
    public string? IncludedTagIds { get; set; }

    /// <summary>
    /// Gets or sets the comma-separated list of excluded tag IDs.
    /// Null or empty means no tag exclusion filter is saved.
    /// </summary>
    public string? ExcludedTagIds { get; set; }

    /// <summary>
    /// Gets or sets the comma-separated list of included player IDs.
    /// Null or empty means no player inclusion filter is saved.
    /// </summary>
    public string? IncludedPlayerIds { get; set; }

    /// <summary>
    /// Gets or sets the comma-separated list of excluded player IDs.
    /// Null or empty means no player exclusion filter is saved.
    /// </summary>
    public string? ExcludedPlayerIds { get; set; }

    /// <summary>Gets or sets the clip status to filter by as a string, or null for all statuses.</summary>
    public string? Status { get; set; }

    /// <summary>Gets or sets whether only favourite clips are shown. Null means all clips.</summary>
    public bool? IsFavourite { get; set; }
}
