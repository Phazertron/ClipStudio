using ClipStudio.Core.Entities;
using ClipStudio.UI.ViewModels;

namespace ClipStudio.Tests.Fakes;

/// <summary>
/// Stands in for the library view so <see cref="BulkEditViewModel"/> can be driven on its own.
/// </summary>
public sealed class FakeBulkEditHost : IBulkEditHost
{
    /// <summary>Gets or sets the clips the library is showing.</summary>
    public List<ClipCardViewModel> ClipList { get; set; } = [];

    /// <summary>Gets or sets the clips currently selected.</summary>
    public List<ClipCardViewModel> SelectionList { get; set; } = [];

    /// <summary>Gets or sets the general tags the chip builder resolves against.</summary>
    public List<Tag> TagList { get; set; } = [];

    /// <summary>Gets or sets the game tags the chip builder resolves against.</summary>
    public List<Tag> GameTagList { get; set; } = [];

    /// <summary>Gets or sets the players the chip builder resolves against.</summary>
    public List<Player> PlayerList { get; set; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<ClipCardViewModel> Clips => ClipList;

    /// <inheritdoc/>
    public IReadOnlyList<ClipCardViewModel> SelectedClips => SelectionList;

    /// <inheritdoc/>
    public IReadOnlyList<Tag> AvailableTags => TagList;

    /// <inheritdoc/>
    public IReadOnlyList<Tag> AvailableGameTags => GameTagList;

    /// <inheritdoc/>
    public IReadOnlyList<Player> AvailablePlayers => PlayerList;

    /// <summary>Gets the number of times a reload was requested.</summary>
    public int ReloadCalls { get; private set; }

    /// <summary>Gets the number of times the selection was cleared.</summary>
    public int DeselectAllCalls { get; private set; }

    /// <summary>Gets the selection changes requested, in order.</summary>
    public List<(ClipCardViewModel Card, bool Selected)> SelectionChanges { get; } = [];

    /// <inheritdoc/>
    public Task ReloadAsync()
    {
        ReloadCalls++;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void DeselectAll()
    {
        DeselectAllCalls++;
        SelectionList.Clear();
    }

    /// <inheritdoc/>
    public void SetClipSelected(ClipCardViewModel card, bool selected)
    {
        SelectionChanges.Add((card, selected));
        if (selected)
        {
            if (!SelectionList.Contains(card)) SelectionList.Add(card);
        }
        else
        {
            SelectionList.Remove(card);
        }
    }
}
