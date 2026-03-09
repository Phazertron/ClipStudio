using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Represents a single entry in the application's left-hand navigation sidebar.
/// Each item maps a label and icon to a page view model.
/// Observable properties allow the nav template to react to scan progress and badge counts.
/// </summary>
public sealed partial class NavigationItemViewModel : ViewModelBase
{
    /// <summary>Gets the display label shown next to the icon in the sidebar.</summary>
    public string Label { get; }

    /// <summary>Gets the Material Design icon used to identify this navigation item.</summary>
    public MaterialIconKind Icon { get; }

    /// <summary>Gets the page view model that is displayed when this item is selected.</summary>
    public ViewModelBase Page { get; }

    /// <summary>
    /// Gets or sets a value indicating whether a background scan or import is in progress
    /// for this navigation item. When true the sidebar icon is replaced with a spinning cog.
    /// </summary>
    [ObservableProperty]
    private bool _isScanning;

    /// <summary>
    /// Gets or sets the badge count overlaid on the nav icon (e.g. number of unreviewed clips).
    /// A value of zero hides the badge.
    /// </summary>
    [ObservableProperty]
    private int _badgeCount;

    /// <summary>
    /// Initialises a new <see cref="NavigationItemViewModel"/>.
    /// </summary>
    /// <param name="label">The human-readable label for this navigation item.</param>
    /// <param name="icon">The Material icon kind to display.</param>
    /// <param name="page">The view model of the page to show when this item is selected.</param>
    public NavigationItemViewModel(string label, MaterialIconKind icon, ViewModelBase page)
    {
        Label = label;
        Icon  = icon;
        Page  = page;
    }
}
