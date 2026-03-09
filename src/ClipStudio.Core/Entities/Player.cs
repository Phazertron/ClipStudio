namespace ClipStudio.Core.Entities;

/// <summary>
/// Represents a player identity (a person who plays in clips) within the ClipStudio library.
/// A player has a display name, optional icon, a set of known aliases, and an optional
/// "IsMe" flag that causes every newly imported clip to be automatically tagged with this player.
/// </summary>
public class Player
{
    /// <summary>Gets or sets the unique identifier of this player.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the primary display name for this player.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute file-system path to the player's icon image.
    /// When null, no icon is shown.
    /// </summary>
    public string? IconPath { get; set; }

    /// <summary>
    /// Gets or sets whether this player represents the local user.
    /// Only one player may have <c>IsMe = true</c> at a time; the service enforces this constraint.
    /// When <c>IsMe</c> is true, every newly imported clip is automatically tagged with this player.
    /// </summary>
    public bool IsMe { get; set; } = false;

    /// <summary>Gets or sets the UTC timestamp when this player was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets the collection of alternative names (aliases) for this player.</summary>
    public ICollection<PlayerAlias> Aliases { get; set; } = new List<PlayerAlias>();

    /// <summary>Gets the collection of join records linking this player to clips.</summary>
    public ICollection<ClipPlayer> ClipPlayers { get; set; } = new List<ClipPlayer>();
}
