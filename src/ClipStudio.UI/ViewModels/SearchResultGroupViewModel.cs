using System.Collections.Generic;
using System.Linq;
using ClipStudio.Application.Models;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// One group of search results as the flyout draws it.
/// </summary>
/// <param name="Title">The heading.</param>
/// <param name="Rows">The rows under it.</param>
/// <param name="MoreCount">How many further results exist beyond those shown.</param>
public sealed record SearchResultGroupViewModel(
    string Title,
    IReadOnlyList<SearchResultRowViewModel> Rows,
    int MoreCount)
{
    /// <summary>Gets whether more results exist than are shown.</summary>
    public bool HasMore => MoreCount > 0;

    /// <summary>Builds a display group from a service result.</summary>
    /// <param name="group">The group the search returned.</param>
    /// <returns>The group, ready to bind.</returns>
    public static SearchResultGroupViewModel From(SearchResultGroup group)
        => new(
            group.Title,
            group.Items.Select(i => new SearchResultRowViewModel(i)).ToList(),
            group.MoreCount);
}
