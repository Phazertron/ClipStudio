namespace ClipStudio.Application.Models;

/// <summary>
/// Two or more clips already in the library whose files hold identical contents.
/// </summary>
/// <param name="FullHash">The full hash the group's members share.</param>
/// <param name="ClipIds">The clips in the group, in the order they were found.</param>
/// <remarks>
/// Identifiers rather than entities: a group can be found long before anyone looks at it, and the
/// clips it names may have been tagged, trashed or relocated in between. Whatever presents the
/// group loads them fresh.
/// </remarks>
public sealed record DuplicateClipGroup(string FullHash, IReadOnlyList<int> ClipIds);
