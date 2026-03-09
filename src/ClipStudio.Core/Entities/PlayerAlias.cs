namespace ClipStudio.Core.Entities;

/// <summary>
/// An alternative name (alias) for a <see cref="Player"/>.
/// Players may have been known by different in-game names across titles or over time.
/// </summary>
public class PlayerAlias
{
    /// <summary>Gets or sets the unique identifier of this alias.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the player this alias belongs to.</summary>
    public int PlayerId { get; set; }

    /// <summary>Gets or sets the player navigation property.</summary>
    public Player Player { get; set; } = null!;

    /// <summary>Gets or sets the alias text (alternative in-game name).</summary>
    public string Alias { get; set; } = string.Empty;
}
