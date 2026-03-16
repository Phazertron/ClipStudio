using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for a single filter chip in the library filter panel.
/// A chip represents one tag, game tag, or player that the user has pinned as a filter.
/// The chip can be toggled between "include" (default) and "exclude" modes.
/// </summary>
public sealed partial class FilterChipViewModel : ViewModelBase
{
    private readonly Action<FilterChipViewModel> _onRemove;
    private readonly Action<FilterChipViewModel> _onChange;

    /// <summary>Gets the database identifier of the entity this chip represents.</summary>
    public int Id { get; }

    /// <summary>Gets the display name shown on the chip.</summary>
    public string Name { get; }

    /// <summary>
    /// Gets or sets a value indicating whether this filter is in exclude mode.
    /// When <see langword="true"/>, clips that match this chip are hidden rather than shown.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TooltipText))]
    private bool _isExcluded;

    /// <summary>Gets the tooltip text reflecting the current include/exclude state.</summary>
    public string TooltipText => IsExcluded ? $"Exclude: {Name}" : $"Include: {Name}";

    /// <summary>Gets the command that toggles between include and exclude mode.</summary>
    public IRelayCommand ToggleExcludeCommand { get; }

    /// <summary>Gets the command that removes this chip from the active filter set.</summary>
    public IRelayCommand RemoveCommand { get; }

    /// <summary>Initialises a new instance of <see cref="FilterChipViewModel"/>.</summary>
    /// <param name="id">The entity identifier.</param>
    /// <param name="name">The display name.</param>
    /// <param name="onRemove">Callback invoked when this chip is removed.</param>
    /// <param name="onChange">Callback invoked when include/exclude is toggled (to trigger a reload).</param>
    public FilterChipViewModel(int id, string name, Action<FilterChipViewModel> onRemove, Action<FilterChipViewModel> onChange)
    {
        Id       = id;
        Name     = name;
        _onRemove = onRemove;
        _onChange = onChange;

        RemoveCommand        = new RelayCommand(() => _onRemove(this));
        ToggleExcludeCommand = new RelayCommand(() =>
        {
            IsExcluded = !IsExcluded;
            _onChange(this);
        });
    }
}
