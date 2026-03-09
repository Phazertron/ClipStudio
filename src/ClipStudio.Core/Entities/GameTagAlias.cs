namespace ClipStudio.Core.Entities;

/// <summary>
/// Maps an OBS-derived game name string (as parsed from a clip filename) to a confirmed
/// <see cref="Tag"/> of type Game.  When a matching alias is found during import, the game tag
/// is automatically applied without requiring user confirmation.
/// </summary>
public class GameTagAlias
{
    /// <summary>Gets or sets the unique identifier of this alias.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the identifier of the Game-type <see cref="Tag"/> this alias maps to.</summary>
    public int TagId { get; set; }

    /// <summary>Gets or sets the Game-type tag navigation property.</summary>
    public Tag Tag { get; set; } = null!;

    /// <summary>
    /// Gets or sets the raw alias string as it appears in the clip filename (e.g., the bracket token
    /// produced by the ClipStudio OBS script: <c>[Apex Legends]</c> → <c>Apex Legends</c>).
    /// Comparison is case-insensitive.
    /// </summary>
    public string AliasString { get; set; } = string.Empty;
}
