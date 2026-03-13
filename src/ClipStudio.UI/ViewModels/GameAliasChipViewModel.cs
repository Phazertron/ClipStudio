using System;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for a single auto-detect alias chip shown in the Games page.
/// Displays the alias string and provides an unlink command.
/// </summary>
public sealed class GameAliasChipViewModel : ViewModelBase
{
    /// <summary>Gets the database identifier of the alias record.</summary>
    public int AliasId { get; }

    /// <summary>Gets the raw alias string as it appears in clip filenames.</summary>
    public string AliasString { get; }

    /// <summary>Gets the command that removes this alias from the game tag.</summary>
    public IRelayCommand UnlinkCommand { get; }

    /// <summary>
    /// Initialises a new <see cref="GameAliasChipViewModel"/>.
    /// </summary>
    /// <param name="aliasId">The database identifier of the alias record.</param>
    /// <param name="aliasString">The raw OBS-derived alias string.</param>
    /// <param name="onUnlink">Callback invoked when the user clicks the unlink button.</param>
    public GameAliasChipViewModel(int aliasId, string aliasString, Action<GameAliasChipViewModel> onUnlink)
    {
        AliasId       = aliasId;
        AliasString   = aliasString;
        UnlinkCommand = new RelayCommand(() => onUnlink(this));
    }
}
