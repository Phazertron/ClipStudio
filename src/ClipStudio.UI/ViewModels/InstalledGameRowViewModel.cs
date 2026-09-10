using ClipStudio.Application.Models;
using ClipStudio.Core.Enums;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// One installed game in the launcher import preview.
/// </summary>
public sealed partial class InstalledGameRowViewModel : ViewModelBase
{
    /// <summary>Gets the game as the scanner found it.</summary>
    public InstalledGame Game { get; }

    /// <summary>Gets the title.</summary>
    public string Name => Game.Name;

    /// <summary>Gets the launcher it came from.</summary>
    public GameLauncher Launcher => Game.Launcher;

    /// <summary>Gets the launcher's identifier for it, if any.</summary>
    public string? StoreAppId => Game.StoreAppId;

    /// <summary>Gets whether the library already has a Game tag with this name.</summary>
    /// <remarks>
    /// An existing tag is matched by name and left alone rather than duplicated, so importing
    /// twice is safe. Such a row is shown, unticked, saying it is already there.
    /// </remarks>
    public bool AlreadyInLibrary { get; }

    /// <summary>Gets or sets whether this game will be created on import.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Gets the reason this row is not ticked by default, or null when it is.</summary>
    public string? Reason { get; }

    /// <summary>Gets whether there is a reason worth showing.</summary>
    public bool HasReason => Reason is not null;

    /// <summary>Initialises a new <see cref="InstalledGameRowViewModel"/>.</summary>
    /// <param name="game">The game the scanner found.</param>
    /// <param name="alreadyInLibrary">Whether a Game tag with this name already exists.</param>
    public InstalledGameRowViewModel(InstalledGame game, bool alreadyInLibrary)
    {
        Game             = game;
        AlreadyInLibrary = alreadyInLibrary;

        // Ticked only when it is a game the library does not already have. Nothing is created
        // without being ticked, so the default is a starting point rather than a decision.
        _isSelected = game.IsLikelyGame && !alreadyInLibrary;

        Reason = alreadyInLibrary
            ? "already in your library"
            : game.ExcludedReason;
    }
}
