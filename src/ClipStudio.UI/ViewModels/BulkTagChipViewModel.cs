using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for a tag, player, or game chip shown in the bulk-edit staging panel.
/// Each chip has a <see cref="BulkTagStatus"/> that controls its colour and behaviour:
/// <list type="bullet">
/// <item><see cref="BulkTagStatus.Shared"/> — purple, present on all selected clips. Has a remove button (removes from all).</item>
/// <item><see cref="BulkTagStatus.Partial"/> — red, present on some clips only. Has both a promote button (add to missing) and a remove button (remove from clips that have it).</item>
/// <item><see cref="BulkTagStatus.New"/> — yellow, added or promoted this session; will be applied on Apply. Has a remove button (unstage).</item>
/// </list>
/// </summary>
public sealed partial class BulkTagChipViewModel : ObservableObject
{
    /// <summary>Gets the database identifier of the entity (tag ID, player ID, or game tag ID).</summary>
    public int EntityId { get; }

    /// <summary>Gets the display name shown on the chip.</summary>
    public string Name { get; }

    /// <summary>Gets or sets the current status of this chip within the bulk-edit session.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShared))]
    [NotifyPropertyChangedFor(nameof(IsPartial))]
    [NotifyPropertyChangedFor(nameof(IsNew))]
    private BulkTagStatus _status;

    /// <summary>Gets whether the chip is in the <see cref="BulkTagStatus.Shared"/> state.</summary>
    public bool IsShared  => Status == BulkTagStatus.Shared;

    /// <summary>Gets whether the chip is in the <see cref="BulkTagStatus.Partial"/> state.</summary>
    public bool IsPartial => Status == BulkTagStatus.Partial;

    /// <summary>Gets whether the chip is in the <see cref="BulkTagStatus.New"/> state.</summary>
    public bool IsNew     => Status == BulkTagStatus.New;

    /// <summary>
    /// Gets the command that promotes a <see cref="BulkTagStatus.Partial"/> chip to
    /// <see cref="BulkTagStatus.New"/> so it will be included in the next Apply operation.
    /// Null for non-Partial chips.
    /// </summary>
    public IRelayCommand? PromoteCommand { get; }

    /// <summary>
    /// Gets the command that removes this chip.
    /// For <see cref="BulkTagStatus.New"/> chips: unstages the pending addition.
    /// For <see cref="BulkTagStatus.Shared"/> chips: immediately removes from all selected clips.
    /// For <see cref="BulkTagStatus.Partial"/> chips: immediately removes from the clips that have it.
    /// </summary>
    public IRelayCommand? RemoveCommand { get; }

    /// <summary>
    /// Initialises a new <see cref="BulkTagChipViewModel"/>.
    /// </summary>
    /// <param name="entityId">The database identifier of the entity (tag, player, or game tag).</param>
    /// <param name="name">The display name shown on the chip.</param>
    /// <param name="status">Initial status of this chip.</param>
    /// <param name="promote">Callback invoked when the user promotes a Partial chip. Pass <see langword="null"/> for non-Partial chips.</param>
    /// <param name="remove">Callback invoked when the user removes the chip. Present on all statuses.</param>
    public BulkTagChipViewModel(
        int entityId,
        string name,
        BulkTagStatus status,
        Action<BulkTagChipViewModel>? promote,
        Action<BulkTagChipViewModel>? remove)
    {
        EntityId = entityId;
        Name     = name;
        _status  = status;

        PromoteCommand = promote is not null
            ? new RelayCommand(() => promote(this))
            : null;

        RemoveCommand = remove is not null
            ? new RelayCommand(() => remove(this))
            : null;
    }
}
