namespace ClipStudio.Application.Models;

/// <summary>
/// The kinds of thing a library-wide search can turn up.
/// </summary>
public enum SearchResultKind
{
    /// <summary>A clip, matched on its file name or notes.</summary>
    Clip = 0,

    /// <summary>A highlight, matched on its label.</summary>
    Highlight = 1,

    /// <summary>A tag, matched on its name. Activating it filters the library by that tag.</summary>
    Tag = 2,

    /// <summary>A game tag, matched on its name. Activating it filters the library by that game.</summary>
    Game = 3,

    /// <summary>A player, matched on their display name.</summary>
    Player = 4,

    /// <summary>A spoken line from a transcript, matched on its text.</summary>
    Caption = 5,
}
