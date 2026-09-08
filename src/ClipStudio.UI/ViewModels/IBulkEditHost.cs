using System.Collections.Generic;
using System.Threading.Tasks;
using ClipStudio.Core.Entities;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// The context <see cref="BulkEditViewModel"/> needs from the library view.
/// </summary>
/// <remarks>
/// Implemented by <see cref="LibraryViewModel"/>. Bulk editing is not self-contained: every
/// operation is defined over the current selection, the chips it builds are resolved against the
/// library's picker collections, and each applied change has to be followed by a reload. Routing
/// those through one seam keeps the dependency one-way - the child never reaches back into the
/// library's filters, sort or paging.
/// </remarks>
public interface IBulkEditHost
{
    /// <summary>Gets the clips currently displayed, in view order.</summary>
    /// <remarks>Needed only by the "remove all broken" action, which works on the whole view.</remarks>
    IReadOnlyList<ClipCardViewModel> Clips { get; }

    /// <summary>Gets the clips currently selected. Every bulk operation is defined over these.</summary>
    IReadOnlyList<ClipCardViewModel> SelectedClips { get; }

    /// <summary>Gets the general tags available for the bulk tag picker and chip resolution.</summary>
    IReadOnlyList<Tag> AvailableTags { get; }

    /// <summary>Gets the game tags available for the bulk game picker and chip resolution.</summary>
    IReadOnlyList<Tag> AvailableGameTags { get; }

    /// <summary>Gets the players available for the bulk player picker and chip resolution.</summary>
    IReadOnlyList<Player> AvailablePlayers { get; }

    /// <summary>Reloads the library so applied changes become visible.</summary>
    Task ReloadAsync();

    /// <summary>Clears the selection, which also cancels any staged edits and copy-format mode.</summary>
    void DeselectAll();

    /// <summary>Selects or deselects a single clip.</summary>
    /// <param name="card">The clip card to change.</param>
    /// <param name="selected">Whether the clip should end up selected.</param>
    void SetClipSelected(ClipCardViewModel card, bool selected);
}
