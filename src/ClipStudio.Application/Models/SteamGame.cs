namespace ClipStudio.Application.Models;

/// <summary>
/// Represents a game entry returned by the Steam Community search API.
/// Used to create Game-type tags without requiring any API credentials.
/// </summary>
public sealed class SteamGame
{
    /// <summary>Gets or sets the Steam App ID of the game.</summary>
    public int AppId { get; set; }

    /// <summary>Gets or sets the official title of the game.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL of the game's library portrait cover art from the Steam CDN,
    /// or null if not available.
    /// </summary>
    public string? CoverUrl { get; set; }
}
