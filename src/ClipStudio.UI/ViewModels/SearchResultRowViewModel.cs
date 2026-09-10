using ClipStudio.Application.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// One search result as the flyout draws it.
/// </summary>
/// <remarks>
/// The result itself is a plain record from the service. This adds the one thing the view needs
/// that the service has no business knowing: whether the keyboard highlight is currently on it.
/// </remarks>
public sealed partial class SearchResultRowViewModel : ViewModelBase
{
    /// <summary>Gets the result this row shows.</summary>
    public SearchResultItem Item { get; }

    /// <summary>Gets the main line.</summary>
    public string Title => Item.Title;

    /// <summary>Gets the supporting line, if any.</summary>
    public string? Subtitle => Item.Subtitle;

    /// <summary>Gets or sets whether the keyboard highlight is on this row.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Initialises a new <see cref="SearchResultRowViewModel"/>.</summary>
    /// <param name="item">The result to show.</param>
    public SearchResultRowViewModel(SearchResultItem item)
    {
        Item = item;
    }
}
