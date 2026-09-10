using ClipStudio.Core.Enums;

namespace ClipStudio.Application.Models;

/// <summary>
/// A game found installed on this machine.
/// </summary>
/// <param name="Name">The title as the launcher records it.</param>
/// <param name="Launcher">Which launcher it was found in.</param>
/// <param name="StoreAppId">
/// The launcher's own identifier for the title, when it has one. For Steam this is the AppId,
/// which is also what cover art is fetched by - so an imported game gets its art without a search.
/// </param>
/// <param name="IsLikelyGame">
/// Whether this looks like something worth tagging clips with. False for the dedicated servers,
/// soundtracks and redistributables that install as separate entries.
/// </param>
/// <param name="ExcludedReason">Why <paramref name="IsLikelyGame"/> is false, for display.</param>
public sealed record InstalledGame(
    string Name,
    GameLauncher Launcher,
    string? StoreAppId = null,
    bool IsLikelyGame = true,
    string? ExcludedReason = null);
