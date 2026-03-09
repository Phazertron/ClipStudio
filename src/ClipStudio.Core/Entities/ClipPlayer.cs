namespace ClipStudio.Core.Entities;

/// <summary>
/// Join entity that records a player's participation in a specific clip.
/// The composite primary key is (<see cref="ClipId"/>, <see cref="PlayerId"/>).
/// </summary>
public class ClipPlayer
{
    /// <summary>Gets or sets the identifier of the clip.</summary>
    public int ClipId { get; set; }

    /// <summary>Gets or sets the clip navigation property.</summary>
    public Clip Clip { get; set; } = null!;

    /// <summary>Gets or sets the identifier of the player.</summary>
    public int PlayerId { get; set; }

    /// <summary>Gets or sets the player navigation property.</summary>
    public Player Player { get; set; } = null!;
}
