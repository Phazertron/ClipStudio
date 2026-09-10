namespace ClipStudio.Application.Models;

/// <summary>
/// The results of one kind, as the flyout groups them.
/// </summary>
/// <param name="Kind">The kind of result this group holds.</param>
/// <param name="Title">The heading shown above the group.</param>
/// <param name="Items">The results, already limited to what will be shown.</param>
/// <param name="TotalCount">
/// How many results there were in total. Larger than the item count when the group was capped, so
/// the flyout can say how many more there are rather than silently hiding them.
/// </param>
public sealed record SearchResultGroup(
    SearchResultKind Kind,
    string Title,
    IReadOnlyList<SearchResultItem> Items,
    int TotalCount)
{
    /// <summary>Gets whether more results exist than are shown.</summary>
    public bool HasMore => TotalCount > Items.Count;

    /// <summary>Gets the count of results not shown.</summary>
    public int MoreCount => Math.Max(0, TotalCount - Items.Count);
}
